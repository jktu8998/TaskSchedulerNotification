using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Newtonsoft.Json;
using TaskStatus = Domain.Enums.TaskStatus; // Используем то, что выбрали ранее для DLQ

namespace Application.Handlers;

/// <summary>
/// Обработчик восстановления зависших заданий.
/// Находит задания в статусе Executing с истекшим LockedUntil.
/// Помечает их как Failed (или Dead) и перепланирует/отправляет в DLQ.
/// Добавлен Jitter к интервалам повторов, чтобы избежать эффекта "гремящего стада".
/// </summary>
public sealed class RunHeartbeatCommandHandler : ICommandHandler<RunHeartbeatCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRandomProvider _random;

    public RunHeartbeatCommandHandler(
        ITaskRepository taskRepo,
        IDeadLetterRepository dlqRepo,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTime,
        IDomainEventDispatcher dispatcher,
        IRandomProvider random)
    {
        _taskRepo = taskRepo;
        _dlqRepo = dlqRepo;
        _unitOfWork = unitOfWork;
        _dateTime = dateTime;
        _dispatcher = dispatcher;
        _random = random;
    }

    public async Task HandleAsync(RunHeartbeatCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;
        
        // Получаем "протухшие" задачи (Status == Executing && LockedUntil <= utcNow)
        var staleTasks = await _taskRepo.GetStaleExecutingTasksAsync(utcNow, cancellationToken);
        
        if (staleTasks.Count == 0)
            return;

        foreach (var task in staleTasks)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                // Помечаем как упавшую. Домен сам инкрементит попытки и решит: Failed или Dead.
                task.MarkFailed(utcNow, "Execution timed out (recovered by Heartbeat)");

                if (task.Status == TaskStatus.Failed)
                {
                    // Вычисляем время следующей попытки и переводим в Scheduled
                    var attemptIndex = task.CurrentAttempt - 1;
                    var baseInterval = attemptIndex < task.RetryPolicy.IntervalsSeconds.Count
                        ? TimeSpan.FromSeconds(task.RetryPolicy.IntervalsSeconds[attemptIndex])
                        : TimeSpan.FromMinutes(1);
                    // Добавляем случайный разброс (Jitter) 0–15 секунд
                    var jitter = TimeSpan.FromSeconds(_random.Next(0, 16));
                    var jitteredInterval = baseInterval + jitter;
                    var nextRetryAt = utcNow + jitteredInterval;
                    task.ScheduleRetry(utcNow, nextRetryAt);
                }
                else if (task.Status == TaskStatus.Dead)
                {
                    // Все попытки исчерпаны — сохраняем в DLQ
                    // Не забываем передать SenderId в конструктор DLQ (как мы обсудили ранее!)
                    var snapshot = JsonConvert.SerializeObject(task);
                    var dlqEntry = new DeadLetterEntry(
                        task.Id, 
                        task.SenderId, // Пробросили SenderId для ролевой модели!
                        snapshot, 
                        "Execution timed out and all retries exhausted", 
                        utcNow);
                        
                    await _dlqRepo.AddAsync(dlqEntry, cancellationToken);
                }

                await _taskRepo.UpdateAsync(task, cancellationToken);
                await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);
                
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                // TODO: Залогировать ошибку восстановления конкретной задачи
            }
            finally
            {
                task.ClearDomainEvents();
            }
        }
    }
}
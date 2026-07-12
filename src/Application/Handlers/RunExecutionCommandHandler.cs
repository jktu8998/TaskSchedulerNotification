using System.Text.Json;
using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды выполнения задания.
/// Реализует трёхфазный алгоритм:
/// 1. Захват (короткая транзакция): переводит задание из Queued в Executing,
///    устанавливает LockedUntil и сразу коммитит, освобождая соединение с БД.
/// 2. Выполнение (без транзакции): совершает HTTP-запрос к внешнему сервису с указанным таймаутом.
/// 3. Фиксация результата (новая короткая транзакция): в зависимости от ответа
///    либо завершает задание успешно (CompleteSuccessfully), либо помечает как
///    проваленное (MarkFailed). Для периодических задач после успеха вызывает
///    Reschedule с новым временем срабатывания. При исчерпании попыток
///    сохраняет снимок задания в Dead Letter Queue.
/// 
/// Гарантирует идемпотентность (игнорирует задачи не в статусе Queued),
/// изолирует транзакции от длительного I/O, а доставку результата
/// (ResultDelivery) выполняет вне транзакции после успешного коммита.
/// </summary>
public sealed class RunExecutionCommandHandler : ICommandHandler<RunExecutionCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IHttpExecutor _httpExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly IRandomProvider _random;    
    
    public RunExecutionCommandHandler(
        ITaskRepository taskRepo,
        IHttpExecutor httpExecutor,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTime,
        IDomainEventDispatcher dispatcher,
        IDeadLetterRepository dlqRepo,
        IRandomProvider random)
    {
        _taskRepo = taskRepo;
        _httpExecutor = httpExecutor;
        _unitOfWork = unitOfWork;
        _dateTime = dateTime;
        _dispatcher = dispatcher;
        _dlqRepo = dlqRepo;
        _random = random;
    }

    public async Task HandleAsync(RunExecutionCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;
        var taskId = TaskId.From(command.TaskId);

        // Загружаем задание
        var task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);
        if (task == null || task.Status != StatusTask.Queued)
            return; // игнорируем дубликаты или неактуальные сообщения

        // ====== ФАЗА 1: Захват (быстрая транзакция) ======
        // Вычисляем таймаут: по умолчанию 30 секунд + 5 секунд буфера для предотвращения гонки с Heartbeat
        var timeoutSeconds = task.Execution.TimeoutSeconds ?? 30;
        var lockDuration = TimeSpan.FromSeconds(timeoutSeconds + 5);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            task.StartExecution(utcNow, lockDuration);
            await _taskRepo.UpdateAsync(task, cancellationToken);
            await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            task.ClearDomainEvents();
            throw; // при ошибке захвата — пробрасываем выше, чтобы воркер мог решить, что делать
        }
        task.ClearDomainEvents();

        // ====== ФАЗА 2: Выполнение HTTP-запроса (без транзакции) ======
        var executionConfig = task.Execution;
        var request = new HttpRequestConfig(
            executionConfig.Method,
            executionConfig.Url,
            executionConfig.Headers,
            executionConfig.Body)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds) // передаём таймаут
        };

        HttpResponseResult response;
        try
        {
            response = await _httpExecutor.ExecuteAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Ошибка самого HTTP-запроса (сеть, таймаут)
            response = new HttpResponseResult(0, ex.Message, false);
        }

        // ====== ФАЗА 3: Фиксация результата (новая быстрая транзакция) ======
        // Перезагружаем задание, чтобы иметь актуальное состояние (хотя LockedUntil не истёк, но для надёжности)
        task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);
        if (task == null || task.Status != StatusTask.Executing)
        {
            // Задание кто-то перехватил (heartbeat) или отменил — игнорируем
            return;
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (response.IsSuccess)
            {
                // Успешное выполнение
                if (task.Type == TaskType.Periodic)
                {
                    var nextExecutionAt = task.Schedule.GetNextOccurrence(utcNow)
                                          ?? throw new InvalidOperationException("Cron expression has no future occurrences.");
                    task.Reschedule(utcNow, nextExecutionAt);
                }
                else
                {
                    task.CompleteSuccessfully(utcNow);
                }
            }
            else
            {
                // Неудача
                task.MarkFailed(utcNow, response.Body ?? "Unknown error");

                if (task.Status == StatusTask.Failed)
                {
                    var attemptIndex = task.CurrentAttempt - 1;
                    // Базовый интервал из RetryPolicy
                    var baseInterval = attemptIndex >= 0 && attemptIndex < task.RetryPolicy.IntervalsSeconds.Count
                        ? TimeSpan.FromSeconds(task.RetryPolicy.IntervalsSeconds[attemptIndex])
                        : TimeSpan.FromMinutes(1);
                    
                    // Добавляем случайный разброс (Jitter) 0–15 секунд
                    var jitter = TimeSpan.FromSeconds(_random.Next(0, 16));
                    var jitteredInterval = baseInterval + jitter;
                    var nextRetryAt = utcNow + jitteredInterval;
                    task.ScheduleRetry(utcNow, nextRetryAt);
                }
                else if (task.Status == StatusTask.Dead)
                {
                    // Попытки исчерпаны — сохраняем в DLQ
                    var snapshot = JsonSerializer.Serialize(task);
                    var dlqEntry = new DeadLetterEntry(task.Id, task.SenderId, snapshot, response.Body ?? "Unknown error", utcNow);
                    await _dlqRepo.AddAsync(dlqEntry, cancellationToken);
                }
            }

            await _taskRepo.UpdateAsync(task, cancellationToken);
            await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            task.ClearDomainEvents();
            throw;
        }
        task.ClearDomainEvents();

        // ====== Доставка результата (вне транзакции, только при успехе) ======
        if (response.IsSuccess && task.ResultDelivery != null)
        {
            // Доставка результата — не критично для задания, ошибки просто логируем
            try
            {
                var deliveryConfig = task.ResultDelivery;
                HttpRequestConfig deliveryRequest;
                if (deliveryConfig.Mode == ResultDeliveryMode.ForwardResponse)
                {
                    deliveryRequest = new HttpRequestConfig(
                        deliveryConfig.Method,
                        deliveryConfig.Url,
                        null,
                        response.Body);
                }
                else // FixedCall
                {
                    deliveryRequest = new HttpRequestConfig(
                        deliveryConfig.Method,
                        deliveryConfig.Url,
                        null,
                        deliveryConfig.Params);
                }

                await _httpExecutor.ExecuteAsync(deliveryRequest, cancellationToken);
            }
            catch
            {
                // TODO: залогировать ошибку доставки, но не откатывать задание
            }
        }
    }
}
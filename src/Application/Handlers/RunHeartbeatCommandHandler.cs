using System.Text.Json;
using Application.Commands;
using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;

namespace Application.Handlers;

/// <summary>
/// Обработчик восстановления зависших заданий (Heartbeat).
/// Находит задачи с истекшим LockedUntil, помечает их как Failed/Dead,
/// и сохраняет все изменения пакетно в одной транзакции.
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
        var staleTasks = await _taskRepo.GetStaleExecutingTasksAsync(utcNow, cancellationToken);

        if (staleTasks.Count == 0) return;

        var tasksToUpdate = new List<ScheduledTask>(staleTasks.Count);
        var dlqEntries = new List<DeadLetterEntry>();

        foreach (var task in staleTasks)
        {
            // 1. Помечаем задание как проваленное (домен сам инкрементит попытки и решит: Failed или Dead)
            task.MarkFailed(utcNow, "Execution timed out (recovered by Heartbeat)");

            if (task.Status == StatusTask.Failed)
            {
                var delay = task.RetryPolicy.GetRetryDelay(task.CurrentAttempt);
                var jitter = TimeSpan.FromSeconds(_random.Next(0, 16));
                task.ScheduleRetry(utcNow, delay + jitter);
            }
            else if (task.Status == StatusTask.Dead)
            {
                // Создаём запись DLQ
                // Создаём DTO-снапшот и сериализуем его
                var snapshotDto = new TaskSnapshotDto
                {
                    IdempotencyKey = task.IdempotencyKey,
                    SenderId = task.SenderId.ToString(),
                    Type = task.Type.ToString(),
                    Schedule = new ScheduleDto
                    {
                        ExecuteAt = task.Schedule.ExecuteAt?.ToString("o"),
                        Offset = task.Schedule.Offset.HasValue ? TaskMapper.FormatOffset(task.Schedule.Offset.Value) : null,
                        Cron = task.Schedule.CronExpression,
                        Timezone = task.Schedule.Timezone
                    },
                    Execution = TaskMapper.MapExecutionToDto(task.Strategy),
                    ResultDelivery = task.ResultDelivery is not null ? new ResultDeliveryConfigDto
                    {
                        Mode = task.ResultDelivery.Mode.ToString(),
                        Url = task.ResultDelivery.Url,
                        Method = task.ResultDelivery.Method,
                        Params = task.ResultDelivery.Params
                    } : null,
                    PollingConfig = task.PollingConfig is not null ? new PollingConfigDto
                    {
                        Field = task.PollingConfig.Field,
                        Condition = task.PollingConfig.Condition,
                        Value = task.PollingConfig.Value,
                        IntervalSeconds = task.PollingConfig.IntervalSeconds,
                        VerboseLogging = task.PollingConfig.VerboseLogging
                    } : null,
                    Retry = new RetryPolicyDto
                    {
                        IntervalsSeconds = task.RetryPolicy.IntervalsSeconds.ToArray()
                    },
                    EncryptedSensitiveData = task.EncryptedSensitiveData,
                    Metadata = task.Metadata.Data.Count > 0
                        ? task.Metadata.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                        : null
                };

                var snapshot = JsonSerializer.Serialize(snapshotDto);

                var dlqEntry = new DeadLetterEntry(
                    task.Id,
                    task.SenderId,
                    snapshot,
                    "Execution timed out and all retries exhausted",
                    utcNow);
                dlqEntries.Add(dlqEntry);
            }

            tasksToUpdate.Add(task);
            _unitOfWork.Track(task); // очистка событий после коммита
        }

        // Сохраняем всё в одной транзакции
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _taskRepo.BulkUpdateAsync(tasksToUpdate, cancellationToken);

            if (dlqEntries.Count > 0)
                await _dlqRepo.BulkAddAsync(dlqEntries, cancellationToken);

            // Диспетчеризуем все накопленные события
            var allEvents = tasksToUpdate.SelectMany(t => t.DomainEvents).ToList();
            await _dispatcher.DispatchAsync(allEvents, cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
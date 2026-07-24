using System.Text.Json;
using Application.Commands;
using Application.Interfaces;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Application.Handlers;

/// <summary>
/// Обработчик выполнения задания.
/// Атомарно захватывает задачу (TryAcquireQueuedTaskAsync), выполняет HTTP-запрос
/// и фиксирует результат в отдельной микро-транзакции.
/// </summary>
public sealed class RunExecutionCommandHandler : ICommandHandler<RunExecutionCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly ITaskLogRepository _taskLogRepo;
    private readonly IHttpExecutor _httpExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly IRandomProvider _random;
    private readonly IOutboxRepository _outboxRepo;
    private readonly ILogger<RunExecutionCommandHandler> _logger;
    public RunExecutionCommandHandler(
        ITaskRepository taskRepo,
        IHttpExecutor httpExecutor,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTime,
        IDomainEventDispatcher dispatcher,
        IDeadLetterRepository dlqRepo,
        IRandomProvider random,
        IOutboxRepository outboxRep,
        ILogger<RunExecutionCommandHandler> logger,
        ITaskLogRepository taskLogRepo)
    {
        _taskRepo = taskRepo;
        _httpExecutor = httpExecutor;
        _unitOfWork = unitOfWork;
        _dateTime = dateTime;
        _dispatcher = dispatcher;
        _dlqRepo = dlqRepo;
        _random = random;
        _outboxRepo = outboxRep;
        _logger = logger;
        _taskLogRepo = taskLogRepo;
    }

    public async Task HandleAsync(RunExecutionCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;
        var taskId = TaskId.From(command.TaskId);

        // -----------------------------------------------------------------
        // ФАЗА 1: Атомарный захват (без предварительной загрузки, без транзакции)
        // Репозиторий выполняет UPDATE ... WHERE status = 'Queued'
        // и возвращает задание с уже установленным LockedUntil.
        // Таймаут выполнения передаётся снаружи, т.к. он ещё не известен.
        // Мы берём значение по умолчанию (30 сек), сам метод TryAcquireQueuedTaskAsync
        // внутри вычислит LockedUntil = utcNow + (timeoutSeconds ?? 30) + 5.
        // -----------------------------------------------------------------
        ScheduledTask? task;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            task = await _taskRepo.TryAcquireQueuedTaskAsync(taskId, utcNow, timeoutSeconds: null, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (task is null) return; // задача уже захвачена, отменена или не существует

        // Проверяем, что стратегия выполнения загружена корректно
        if (task.Execution is null)
        {
            _logger.LogError("Task {TaskId} has null strategy, cannot execute. Moving to dead letter.", task.Id);
            // Можно сразу пометить как Dead и сохранить через транзакцию
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                task.MarkFailed(utcNow, "Execution strategy is missing or corrupted");
                // если статус Dead, добавляем в DLQ
                if (task.Status == StatusTask.Dead)
                {
                    var snapshot = JsonSerializer.Serialize(TaskMapper.ToSnapshot(task));
                    await _dlqRepo.AddAsync(new DeadLetterEntry(task.Id, task.SenderId, snapshot, "Missing strategy", utcNow), cancellationToken);
                }
                await _taskRepo.UpdateAsync(task, task.Version, cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }
            return;
        }
        // После захвата в задаче уже актуальный Status=Executing и LockedUntil
        // Можно выполнять HTTP-запрос
        var strategy = task.Execution;
        var timeout = strategy.TimeoutSeconds ?? 30;

        var request = new HttpRequestConfig(
            strategy is HttpExecutionConfig http ? http.Method : "POST",
            strategy is HttpExecutionConfig httpCfg ? httpCfg.Url : string.Empty,
            strategy is HttpExecutionConfig hdr ? hdr.Headers : null,
            strategy is HttpExecutionConfig bdy ? bdy.Body : null)
        {
            Timeout = TimeSpan.FromSeconds(timeout)
        };

        HttpResponseResult response;
        try
        {
            response = await _httpExecutor.ExecuteAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            response = new HttpResponseResult(0, ex.Message, false);
        }

        // -----------------------------------------------------------------
        // ФАЗА 2: Фиксация результата (микро-транзакция)
        // Перезагружать задачу не нужно, т.к. мы её захватили и никто другой не менял.
        // -----------------------------------------------------------------
        
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (response.IsSuccess)
            {
                if (task.Type == TaskType.Periodic)
                {
                    // var nextExecutionAt = task.Schedule.GetNextOccurrence(utcNow)
                    //     ?? throw new InvalidOperationException("Cron expression has no future occurrences.");
                    task.Reschedule(utcNow);
                }
                else
                {
                    task.CompleteSuccessfully(utcNow);
                }
            }
            else
            {
                task.MarkFailed(utcNow, response.Body ?? "Unknown error");

                if (task.Status == StatusTask.Failed)
                {
                    var delay = task.RetryPolicy.GetRetryDelay(task.CurrentAttempt);
                    var jitter = TimeSpan.FromSeconds(_random.Next(0, 16));
                    task.ScheduleRetry(utcNow, delay + jitter);
                }
                else if (task.Status == StatusTask.Dead)
                {
                    // Создаём DTO-снапшот
                    var snapshotDto = TaskMapper.ToSnapshot(task);
                    var snapshot = JsonSerializer.Serialize(snapshotDto);
                    var dlqEntry = new DeadLetterEntry(task.Id, task.SenderId, snapshot,
                        response.Body ?? "Unknown error", utcNow);
                    await _dlqRepo.AddAsync(dlqEntry, cancellationToken);
                }
            }
            if (response.IsSuccess && task.ResultDelivery is not null)
            {
                var deliveryPayload = JsonSerializer.Serialize(new
                {
                    Mode = task.ResultDelivery.Mode.ToString(),
                    Url = task.ResultDelivery.Url,
                    Method = task.ResultDelivery.Method,
                    Body = task.ResultDelivery.Mode == ResultDeliveryMode.ForwardResponse
                        ? response.Body
                        : task.ResultDelivery.Params
                });

                var outboxMessage = new OutboxMessage(
                    task.Id,
                    "ResultDeliveryRequested",
                    deliveryPayload,
                    utcNow,
                    maxRetries: 3);

                await _outboxRepo.AddAsync(outboxMessage, cancellationToken);
            }
            var responseLog = new TaskLog(
                task.Id,
                "HttpResponse",
                utcNow,
                $"HTTP {response.StatusCode}",
                response.Body
            );
            await _taskLogRepo.AddAsync(responseLog, cancellationToken);
            _unitOfWork.Track(task);// возможно оно не на своем месте и должно быть после коммита 
            await _taskRepo.UpdateAsync(task,task.Version, cancellationToken);
            await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        
    }
}
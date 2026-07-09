using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Cronos;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды изменения задания.
/// Отменяет существующее задание и создаёт новое с обновлёнными полями,
/// после чего сохраняет оба изменения в одной транзакции.
/// Возвращает идентификатор нового задания.
/// </summary>
public sealed class UpdateTaskCommandHandler : ICommandHandler<UpdateTaskCommand, Guid>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IEncryptionService _encryption;
    private readonly IDateTimeProvider _dateTime;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;

    public UpdateTaskCommandHandler(
        ITaskRepository taskRepo,
        IEncryptionService encryption,
        IDateTimeProvider dateTime,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        IDomainEventDispatcher dispatcher)
    {
        _taskRepo = taskRepo;
        _encryption = encryption;
        _dateTime = dateTime;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
    }

    public async Task<Guid> HandleAsync(UpdateTaskCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;
        var oldTaskId = TaskId.From(command.TaskId);
        var oldTask = await _taskRepo.GetByIdAsync(oldTaskId, cancellationToken);

        if (oldTask == null || oldTask.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Task not found or access denied.");

        // 1. Отменяем текущее задание
        oldTask.Cancel(utcNow);

        // 2. Создаём новое задание на основе DTO
        var req = command.UpdatedFields;
        var schedule =ScheduleMapper.MapSchedule(req.Schedule);
        var execution = new ExecutionConfig(req.Execution.Method, req.Execution.Url,
            req.Execution.Headers, req.Execution.Body);

        ResultDeliveryConfig? resultDelivery = null;
        if (req.ResultDelivery != null)
        {
            var mode = Enum.Parse<ResultDeliveryMode>(req.ResultDelivery.Mode, ignoreCase: true);
            resultDelivery = new ResultDeliveryConfig(mode, req.ResultDelivery.Url,
                req.ResultDelivery.Method, req.ResultDelivery.Params);
        }

        PollingConfig? pollingConfig = null;
        if (req.PollingConfig != null)
        {
            pollingConfig = new PollingConfig(req.PollingConfig.Field, req.PollingConfig.Condition,
                req.PollingConfig.Value, req.PollingConfig.IntervalSeconds, req.PollingConfig.VerboseLogging);
        }

        RetryPolicy retryPolicy = req.Retry?.IntervalsSeconds is { Length: > 0 } intervals
            ? new RetryPolicy(intervals)
            : RetryPolicy.Default;

        string? encrypted = null;
        if (!string.IsNullOrWhiteSpace(req.SensitiveData))
            encrypted = _encryption.Encrypt(req.SensitiveData);

        var newTask = new ScheduledTask(
            TaskId.New(),
            _requestContext.SenderId,
            Enum.Parse<TaskType>(req.Type, ignoreCase: true),
            schedule,
            execution,
            resultDelivery,
            pollingConfig,
            retryPolicy,
            encrypted,
            utcNow);

        var nextExecutionAt = newTask.Schedule.GetNextOccurrence(newTask.CreatedAt)
            ?? throw new InvalidOperationException("Cron expression has no future occurrences.");
        newTask.ScheduleTask(utcNow, nextExecutionAt);

        // 3. Сохраняем оба изменения в одной транзакции
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _taskRepo.UpdateAsync(oldTask, cancellationToken);
            await _dispatcher.DispatchAsync(oldTask.DomainEvents, cancellationToken);

            await _taskRepo.AddAsync(newTask, cancellationToken);
            await _dispatcher.DispatchAsync(newTask.DomainEvents, cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            oldTask.ClearDomainEvents();
            newTask.ClearDomainEvents();
            throw;
        }

        oldTask.ClearDomainEvents();
        newTask.ClearDomainEvents();
        return newTask.Id.Value;
    }

    
}
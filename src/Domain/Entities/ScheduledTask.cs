using System;
using System.Collections.Generic;
using Domain.Enums;
using Domain.ValueObjects;
using Domain.DomainEvents;
using TaskStatus = Domain.Enums.TaskStatus;

namespace  Domain.Entities;

public sealed class ScheduledTask
{
    public TaskId Id { get; private set; }
    public string SenderId { get; private set; }
    public TaskType Type { get; private set; }
    public TaskStatus Status { get; private set; }
    public Schedule Schedule { get; private set; }
    public ExecutionConfig Execution { get; private set; }
    public ResultDeliveryConfig? ResultDelivery { get; private set; }
    public PollingConfig? PollingConfig { get; private set; }
    public RetryPolicy RetryPolicy { get; private set; }
    public string? EncryptedSensitiveData { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private ScheduledTask() { /* для ORM/Dapper */ }

    public ScheduledTask(
        TaskId id,
        string senderId,
        TaskType type,
        Schedule schedule,
        ExecutionConfig execution,
        ResultDeliveryConfig? resultDelivery,
        PollingConfig? pollingConfig,
        RetryPolicy? retryPolicy,
        string? encryptedSensitiveData)
    {
        Id = id;
        SenderId = senderId;
        Type = type;
        Status = TaskStatus.Created;
        Schedule = schedule;
        Execution = execution;
        ResultDelivery = resultDelivery;
        PollingConfig = pollingConfig;
        RetryPolicy = retryPolicy ?? RetryPolicy.Default;
        EncryptedSensitiveData = encryptedSensitiveData;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        _domainEvents.Add(new TaskCreatedEvent(this));
    }

    public void ScheduleTask()
    {
        if (Status != TaskStatus.Created)
            throw new InvalidOperationException($"Cannot schedule task in status {Status}");
        Status = TaskStatus.Scheduled;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new TaskScheduledEvent(Id));
    }

    public void Enqueue()
    {
        if (Status != TaskStatus.Scheduled)
            throw new InvalidOperationException($"Cannot enqueue task in status {Status}");
        Status = TaskStatus.Queued;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new TaskQueuedEvent(Id));
    }

    public void StartExecution()
    {
        if (Status != TaskStatus.Queued)
            throw new InvalidOperationException($"Cannot start execution in status {Status}");
        Status = TaskStatus.Executing;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new TaskExecutionStartedEvent(Id));
    }

    public void CompleteSuccessfully()
    {
        if (Status != TaskStatus.Executing)
            throw new InvalidOperationException($"Cannot complete task in status {Status}");
        Status = TaskStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new TaskCompletedEvent(Id));
    }

    public void MarkFailed(int remainingRetries)
    {
        if (Status != TaskStatus.Executing)
            throw new InvalidOperationException($"Cannot fail task in status {Status}");
        if (remainingRetries <= 0)
        {
            Status = TaskStatus.Dead;
            _domainEvents.Add(new TaskMovedToDlqEvent(Id));
        }
        else
        {
            Status = TaskStatus.Failed;
            _domainEvents.Add(new TaskFailedEvent(Id));
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void Pause()
    {
        if (Status != TaskStatus.Scheduled)
            throw new InvalidOperationException($"Cannot pause task in status {Status}");
        Status = TaskStatus.Paused;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new TaskPausedEvent(Id));
    }

    public void Resume()
    {
        if (Status != TaskStatus.Paused)
            throw new InvalidOperationException($"Cannot resume task in status {Status}");
        Status = TaskStatus.Scheduled;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new TaskResumedEvent(Id));
    }

    public void Cancel()
    {
        if (Status == TaskStatus.Completed || Status == TaskStatus.Dead || Status == TaskStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel task in final status {Status}");
        Status = TaskStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new TaskCancelledEvent(Id));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
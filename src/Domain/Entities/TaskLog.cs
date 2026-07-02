using System;
using Domain.ValueObjects;

namespace  Domain.Entities;

public sealed class TaskLog
{
    public long Id { get; private set; }
    public TaskId TaskId { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string EventType { get; private set; } // "Created", "Scheduled", "Executing", "Completed", etc.
    public string? Message { get; private set; }
    public string? Details { get; private set; } // JSON

    public TaskLog(TaskId taskId, string eventType, string? message = null, string? details = null)
    {
        TaskId = taskId;
        Timestamp = DateTime.UtcNow;
        EventType = eventType;
        Message = message;
        Details = details;
    }
}
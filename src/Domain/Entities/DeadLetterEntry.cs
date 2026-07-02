using System;
using  Domain.ValueObjects;

namespace  Domain.Entities;

public sealed class DeadLetterEntry
{
    public long Id { get; private set; }
    public TaskId TaskId { get; private set; }
    public string OriginalTaskSnapshot { get; private set; } // JSON
    public string? ErrorDetails { get; private set; }
    public DateTime MovedAt { get; private set; }

    public DeadLetterEntry(TaskId taskId, string originalTaskSnapshot, string? errorDetails)
    {
        TaskId = taskId;
        OriginalTaskSnapshot = originalTaskSnapshot;
        ErrorDetails = errorDetails;
        MovedAt = DateTime.UtcNow;
    }
}
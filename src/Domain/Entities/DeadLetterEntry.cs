using System;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Запись в Dead Letter Queue. Создаётся, когда задание исчерпало все повторные попытки.
/// Хранит полный снимок задания (OriginalTaskSnapshot) и описание ошибки.
/// Время перемещения передаётся явно.
/// </summary>
public sealed class DeadLetterEntry
{
    public long Id { get; private set; }
    public TaskId TaskId { get; private set; }
    public string OriginalTaskSnapshot { get; private set; }
    public string? ErrorDetails { get; private set; }
    public DateTime MovedAt { get; private set; }

    public DeadLetterEntry(TaskId taskId, string originalTaskSnapshot, string? errorDetails, DateTime utcNow)
    {
        TaskId = taskId;
        OriginalTaskSnapshot = originalTaskSnapshot;
        ErrorDetails = errorDetails;
        MovedAt = utcNow;
    }
}
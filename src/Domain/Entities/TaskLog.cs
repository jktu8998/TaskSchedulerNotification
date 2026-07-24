using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Запись в логе задания. Фиксирует изменение статуса или ошибку.
/// Создаётся только с указанием времени извне.
/// </summary>
public sealed class TaskLog
{
    public long Id { get; private set; }
    public TaskId TaskId { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string EventType { get; private set; }
    public string? Message { get; private set; }
    public string? Details { get; private set; }

    public TaskLog(TaskId taskId, string eventType, DateTime utcNow, string? message = null, string? details = null)
    {
        TaskId = taskId;
        EventType = eventType;
        Timestamp = utcNow;
        Message = message;
        Details = details;
    }
    public TaskLog() { } // для Dapper
}
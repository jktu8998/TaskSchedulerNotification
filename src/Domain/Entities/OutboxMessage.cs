using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Запись в таблице исходящих сообщений (Transactional Outbox).
/// Фиксирует тип события и его сериализованные данные для публикации в очередь сообщений.
/// Используется фоновым воркером OutboxRelay для надёжной доставки.
/// Записи удаляются после успешной отправки.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Уникальный идентификатор записи (первичный ключ).</summary>
    public Guid Id { get; private set; }

    /// <summary>Идентификатор задания, с которым связано событие.</summary>
    public TaskId TaskId { get; private set; }

    /// <summary>
    /// Тип события (например, "TaskCreatedEvent", "TaskFailedEvent").
    /// Используется потребителем для выбора обработчика.
    /// </summary>
    public string EventType { get; private set; }

    /// <summary>
    /// Сериализованные данные события (JSON, Protobuf и т.п.).
    /// Может быть null для событий без данных.
    /// </summary>
    public string? Payload { get; private set; }

    /// <summary>Время создания записи (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Создаёт новое исходящее сообщение.
    /// </summary>
    /// <param name="taskId">Идентификатор задания.</param>
    /// <param name="eventType">Тип события (обязательный, непустой).</param>
    /// <param name="payload">Сериализованное содержимое события (опционально).</param>
    /// <param name="createdAt">Текущее время (передаётся извне для тестируемости).</param>
    public OutboxMessage(TaskId taskId, string eventType, string? payload, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType cannot be null or empty.", nameof(eventType));

        Id = Guid.NewGuid();
        TaskId = taskId;
        EventType = eventType;
        Payload = payload;
        CreatedAt = createdAt;
    }

    private OutboxMessage() { } // для Dapper
}
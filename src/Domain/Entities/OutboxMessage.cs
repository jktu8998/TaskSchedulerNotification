using System;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Запись в таблице исходящих сообщений (Transactional Outbox).
/// Фиксирует факт, что задание с идентификатором TaskId должно быть
/// опубликовано в очередь сообщений. Используется фоновым воркером OutboxRelay
/// для надёжной доставки даже при сбоях брокера.
/// Записи удаляются после успешной отправки.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Уникальный идентификатор записи (первичный ключ).</summary>
    public Guid Id { get; private set; }

    /// <summary>Идентификатор задания, которое нужно отправить в очередь.</summary>
    public TaskId TaskId { get; private set; }

    /// <summary>Время создания записи (UTC). Используется для сортировки при выборке.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Создаёт новое исходящее сообщение.
    /// </summary>
    /// <param name="taskId">Идентификатор задания.</param>
    /// <param name="createdAt">Текущее время (передаётся извне для тестируемости).</param>
    public OutboxMessage(TaskId taskId, DateTime createdAt)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        CreatedAt = createdAt;
    }
}
using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Выполнение завершилось ошибкой, но остались повторные попытки.</summary>
public sealed record TaskFailedEvent : IDomainEvent
{
    public TaskId TaskId { get; }
    public bool IsIntermediate { get; }

    /// <summary>
    /// Создаёт событие ошибки.
    /// </summary>
    /// <param name="taskId">Идентификатор задания.</param>
    /// <param name="isIntermediate">
    /// true — промежуточная ошибка (повторные попытки ещё есть),
    /// false — финальная ошибка (обычно не используется, для финала есть TaskMovedToDlqEvent).
    /// </param>
    public TaskFailedEvent(TaskId taskId, bool isIntermediate)
    {
        TaskId = taskId;
        IsIntermediate = isIntermediate;
    }
}
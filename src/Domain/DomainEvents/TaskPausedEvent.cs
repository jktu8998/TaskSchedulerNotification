using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Задание приостановлено пользователем.</summary>
public sealed record TaskPausedEvent(TaskId TaskId) : IDomainEvent
{
    public bool IsIntermediate => false; // значимое событие
}

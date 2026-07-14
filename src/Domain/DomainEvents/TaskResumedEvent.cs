using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Задание возобновлено после приостановки.</summary>
public sealed record TaskResumedEvent(TaskId TaskId) : IDomainEvent
{
    public bool IsIntermediate => false; // значимое событие
}

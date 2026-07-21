using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

/// <summary>Цепочка приостановлена.</summary>
public sealed record ChainPausedEvent(TaskId ChainId) : IDomainEvent
{
    public TaskId TaskId => ChainId;
    public bool IsIntermediate => false;
}
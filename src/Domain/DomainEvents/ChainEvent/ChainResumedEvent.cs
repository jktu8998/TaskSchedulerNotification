using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

/// <summary>Цепочка возобновлена.</summary>
public sealed record ChainResumedEvent(TaskId ChainId) : IDomainEvent
{
    public TaskId TaskId => ChainId;
    public bool IsIntermediate => false;
}
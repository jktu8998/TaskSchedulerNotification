using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

/// <summary>Цепочка отменена.</summary>
public sealed record ChainCancelledEvent(TaskId ChainId) : IDomainEvent
{
    public TaskId TaskId => ChainId;
    public bool IsIntermediate => false;
}
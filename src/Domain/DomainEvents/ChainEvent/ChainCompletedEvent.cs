using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

/// <summary>Цепочка полностью завершена (успешно).</summary>
public sealed record ChainCompletedEvent(TaskId ChainId) : IDomainEvent
{
    public TaskId TaskId => ChainId;
    public bool IsIntermediate => false;
}
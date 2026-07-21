using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

/// <summary>Цепочка создана и запущена.</summary>
public sealed record ChainStartedEvent(TaskId ChainId) : IDomainEvent
{
    public TaskId TaskId => ChainId; // для совместимости с IDomainEvent (ожидает TaskId, но мы можем использовать ChainId как TaskId)
    public bool IsIntermediate => false;
}
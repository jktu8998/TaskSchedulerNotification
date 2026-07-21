using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

/// <summary>Цепочка остановлена из-за фатальной ошибки.</summary>
public sealed record ChainFailedEvent(TaskId ChainId, string? Reason) : IDomainEvent
{
    public TaskId TaskId => ChainId;
    public bool IsIntermediate => false;
}
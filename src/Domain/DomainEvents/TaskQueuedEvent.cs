using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Задание помещено во внутреннюю очередь на выполнение.</summary>
public sealed record TaskQueuedEvent(TaskId TaskId) : IDomainEvent;

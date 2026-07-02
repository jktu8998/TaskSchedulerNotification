using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskQueuedEvent(TaskId TaskId) : IDomainEvent;

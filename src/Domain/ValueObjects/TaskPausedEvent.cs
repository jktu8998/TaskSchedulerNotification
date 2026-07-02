using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskPausedEvent(TaskId TaskId) : IDomainEvent;

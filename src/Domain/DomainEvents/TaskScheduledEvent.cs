using Domain.ValueObjects;

namespace Domain.DomainEvents;

public sealed record TaskScheduledEvent(TaskId TaskId) : IDomainEvent;

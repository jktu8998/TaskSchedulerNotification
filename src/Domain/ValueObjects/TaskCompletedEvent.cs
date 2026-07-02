using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskCompletedEvent(TaskId TaskId) : IDomainEvent;

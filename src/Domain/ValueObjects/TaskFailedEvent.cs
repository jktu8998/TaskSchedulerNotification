using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskFailedEvent(TaskId TaskId) : IDomainEvent;

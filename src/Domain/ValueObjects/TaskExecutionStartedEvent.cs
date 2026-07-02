using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskExecutionStartedEvent(TaskId TaskId) : IDomainEvent;

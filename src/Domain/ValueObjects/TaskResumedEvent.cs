using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskResumedEvent(TaskId TaskId) : IDomainEvent;

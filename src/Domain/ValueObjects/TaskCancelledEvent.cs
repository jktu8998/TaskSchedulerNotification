using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskCancelledEvent(TaskId TaskId) : IDomainEvent;

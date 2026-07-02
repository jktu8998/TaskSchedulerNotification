using Domain.DomainEvents;

namespace Domain.ValueObjects;

public sealed record TaskMovedToDlqEvent(TaskId TaskId) : IDomainEvent;

using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Исполнитель начал выполнение задания.</summary>
public sealed record TaskExecutionStartedEvent(TaskId TaskId) : IDomainEvent;

using Domain.DomainEvents;
using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Все попытки исчерпаны, задание перемещено в Dead Letter Queue.</summary>
public sealed record TaskMovedToDlqEvent(TaskId TaskId) : IDomainEvent;

using Domain.DomainEvents;
using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Задание успешно выполнено.</summary>
public sealed record TaskCompletedEvent(TaskId TaskId) : IDomainEvent;

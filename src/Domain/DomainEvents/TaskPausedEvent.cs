using Domain.DomainEvents;
using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Задание приостановлено пользователем.</summary>
public sealed record TaskPausedEvent(TaskId TaskId) : IDomainEvent;

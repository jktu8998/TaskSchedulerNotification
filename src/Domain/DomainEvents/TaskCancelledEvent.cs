using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Задание отменено пользователем или системой.</summary>
public sealed record TaskCancelledEvent(TaskId TaskId) : IDomainEvent;

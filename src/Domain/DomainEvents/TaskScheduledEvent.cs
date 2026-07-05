using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>
/// Событие: задание запланировано (переведено из Created в Scheduled).
/// </summary>
public sealed record TaskScheduledEvent(TaskId TaskId) : IDomainEvent;

using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>
/// Маркерный интерфейс для доменных событий.
/// Все события, происходящие внутри сущностей, должны реализовывать этот интерфейс.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Идентификатор задания, с которым связано событие.</summary>
    TaskId TaskId { get; }
}
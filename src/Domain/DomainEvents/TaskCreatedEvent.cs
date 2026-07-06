using Domain.Entities;
using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>
/// Событие: новое задание создано.
/// Содержит полный объект задания для обработчиков.
/// </summary>
public sealed record TaskCreatedEvent(ScheduledTask Task) : IDomainEvent
{
    // Явная реализация, требуемая интерфейсом
    public TaskId TaskId => Task.Id;
}

using Domain.Entities;

namespace Domain.DomainEvents;

/// <summary>
/// Событие: новое задание создано.
/// Содержит полный объект задания для обработчиков.
/// </summary>
public sealed record TaskCreatedEvent(ScheduledTask Task) : IDomainEvent;

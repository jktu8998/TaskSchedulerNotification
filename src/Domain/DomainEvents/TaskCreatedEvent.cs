using Domain.Entities;
using Domain.ValueObjects;

namespace Domain.DomainEvents;


/// <summary>
/// Событие: новое задание создано.
/// Содержит только идентификатор задания.
/// Остальные данные при необходимости запрашиваются из хранилища.
/// </summary>
public sealed record TaskCreatedEvent(TaskId TaskId) : IDomainEvent;

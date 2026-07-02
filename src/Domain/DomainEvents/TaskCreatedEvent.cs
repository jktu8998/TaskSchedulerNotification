using Domain.Entities;

namespace Domain.DomainEvents;

public sealed record TaskCreatedEvent(ScheduledTask Task) : IDomainEvent;

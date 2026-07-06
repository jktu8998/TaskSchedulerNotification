using Domain.DomainEvents;

namespace Application.Handlers;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events);

}
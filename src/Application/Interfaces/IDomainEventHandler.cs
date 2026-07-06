using System.Threading;
using System.Threading.Tasks;
using Domain.DomainEvents;

namespace Application.Interfaces;

/// <summary>
/// Контракт для обработчика доменного события конкретного типа.
/// Реализации регистрируются в DI и вызываются диспетчером событий.
/// </summary>
/// <typeparam name="TEvent">Тип доменного события.</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
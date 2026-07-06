using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain.DomainEvents;

namespace Application.Interfaces;

/// <summary>
/// Диспетчер доменных событий.
/// Принимает коллекцию событий и передаёт их зарегистрированным обработчикам.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Отправить все события из коллекции их обработчикам.
    /// </summary>
    /// <param name="domainEvents">События, сгенерированные агрегатом.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
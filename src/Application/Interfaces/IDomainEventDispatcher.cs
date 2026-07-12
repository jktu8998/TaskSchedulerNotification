
using Domain.DomainEvents;

namespace Application.Interfaces;

/// <summary>
/// Диспетчер доменных событий.
/// Принимает коллекцию событий и передаёт их зарегистрированным обработчикам.
/// </summary>
/// <summary>
/// Центральный диспетчер доменных событий.
/// 
/// Отвечает за маршрутизацию событий, сгенерированных агрегатами, 
/// к зарегистрированным обработчикам (<see cref="IDomainEventHandler{TEvent}"/>).
/// 
/// Использование:
/// После того как Command Handler сохранил агрегат в БД (но до коммита транзакции),
/// он вызывает <see cref="DispatchAsync"/> для всех событий, накопленных в агрегате.
/// Диспетчер находит все обработчики, подходящие по типу события, и выполняет их 
/// последовательно в том же контексте транзакции.
/// 
/// Это позволяет реализовать реактивное логирование, отправку уведомлений 
/// и другие побочные действия без завязки Command Handler'ов на конкретные 
/// инфраструктурные детали.
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
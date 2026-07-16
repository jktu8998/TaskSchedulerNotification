using System.Collections.Concurrent;
using System.Linq.Expressions;
using Application.Interfaces;
using Domain.DomainEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.DomainEvents;

/// <summary>
/// Реализация диспетчера доменных событий.
/// Для каждого события находит все реализации IDomainEventHandler&lt;TEvent&gt;
/// и вызывает их последовательно. Использует кэширование делегатов для производительности.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    // Кэш делегатов: ключ = тип события, значение = делегат, принимающий обработчик и событие
    private static readonly ConcurrentDictionary<Type, Delegate> _handlerInvokers = new();

    public DomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var evt in domainEvents)
        {
            var eventType = evt.GetType();
            
            // Получаем тип IEnumerable<IDomainEventHandler<TEvent>>
            var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(
                typeof(IDomainEventHandler<>).MakeGenericType(eventType));
            
            // Запрашиваем обработчики из DI
            var handlers = (IEnumerable<object>)_serviceProvider.GetRequiredService(handlerEnumerableType);
            
            // Получаем (или создаём) делегат для вызова HandleAsync
            var invoker = GetOrCreateInvoker(eventType);
            
            foreach (var handler in handlers)
            {
                await invoker(handler, evt, cancellationToken);
            }
        }
    }

    // Делегат: (handler, event, ct) -> Task
    private delegate Task HandlerInvoker(object handler, IDomainEvent domainEvent, CancellationToken ct);

    private static HandlerInvoker GetOrCreateInvoker(Type eventType)
    {
        return (HandlerInvoker)_handlerInvokers.GetOrAdd(eventType, type =>
        {
            // handlerType = IDomainEventHandler<TEvent>
            var handlerInterface = typeof(IDomainEventHandler<>).MakeGenericType(type);
            
            // Параметры для лямбды: (object handler, IDomainEvent evt, CancellationToken ct)
            var handlerParam = Expression.Parameter(typeof(object), "handler");
            var eventParam = Expression.Parameter(typeof(IDomainEvent), "evt");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");
            
            // Приводим handler к конкретному типу
            var typedHandler = Expression.Convert(handlerParam, handlerInterface);
            
            // Приводим event к TEvent
            var typedEvent = Expression.Convert(eventParam, type);
            
            // Получаем метод HandleAsync
            var method = handlerInterface.GetMethod("HandleAsync")!;
            
            // Вызов: ((IDomainEventHandler<TEvent>)handler).HandleAsync((TEvent)evt, ct)
            var call = Expression.Call(typedHandler, method, typedEvent, ctParam);
            
            // Создаём лямбду: (handler, evt, ct) => call
            return Expression.Lambda<HandlerInvoker>(call, handlerParam, eventParam, ctParam).Compile();
        });
    }
}
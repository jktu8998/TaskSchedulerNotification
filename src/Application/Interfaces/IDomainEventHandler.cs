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

/*
ВНИМАНИЕ: Все реализации IDomainEventHandler<T> выполняются синхронно в рамках открытой транзакции БД.
Внутри хендлеров ЗАПРЕЩЕНО выполнять сетевые запросы, долгие I/O операции или обращения к сторонним API.
Разрешены только быстрые операции с БД в рамках текущего IUnitOfWork
*/
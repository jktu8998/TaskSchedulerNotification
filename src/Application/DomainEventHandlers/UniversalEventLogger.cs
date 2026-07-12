
using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Interfaces;

namespace Application.DomainEventHandlers;

/// <summary>
/// Универсальный обработчик логирования для всех доменных событий.
/// Имя события вычисляется из имени класса события (отсекается суффикс "Event").
/// Регистрируется в DI как open-generic: typeof(IDomainEventHandler<>), typeof(UniversalEventLogger<>).
/// </summary>
public sealed class UniversalEventLogger<TEvent> : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    private readonly ITaskLogRepository _logRepo;
    private readonly IDateTimeProvider _dateTime;

    // Статическое поле инициализируется один раз для каждого типа TEvent
    private static readonly string EventType = typeof(TEvent).Name.Replace("Event", "");

    public UniversalEventLogger(ITaskLogRepository logRepo, IDateTimeProvider dateTime)
    {
        _logRepo = logRepo;
        _dateTime = dateTime;
    }

    public async Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
    {
        // Быстрая сериализация самого события для сохранения контекста
        var detailsJson = System.Text.Json.JsonSerializer.Serialize(domainEvent);

        var log = new TaskLog(
            domainEvent.TaskId,
            EventType,
            _dateTime.UtcNow,
            details: detailsJson); // Пишем контекст в лог

        await _logRepo.AddAsync(log, cancellationToken);
    }
}
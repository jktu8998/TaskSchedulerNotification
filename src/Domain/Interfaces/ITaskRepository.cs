
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Interfaces;

/// <summary>
/// Контракт (порт) для доступа к хранилищу заданий.
/// Реализуется в слое Infrastructure.
/// </summary>
public interface ITaskRepository
{
    /// <summary>Добавить новое задание в хранилище.</summary>
    Task AddAsync(ScheduledTask task, CancellationToken cancellationToken = default);

    /// <summary>Получить задание по идентификатору. Возвращает null, если не найдено.</summary>
    Task<ScheduledTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить задания, созданные конкретным сервисом-отправителем.
    /// Поддерживает фильтрацию по статусу и типу, пагинацию.
    /// </summary>
    Task<IReadOnlyList<ScheduledTask>> GetBySenderIdAsync(
        SenderId senderId, int skip, int take, 
        StatusTask? status = null, TaskType? type = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить задания, у которых статус Scheduled и время выполнения наступило.
    /// Используется планировщиком для отправки в очередь.
    /// </summary>
    Task<IReadOnlyList<ScheduledTask>> GetScheduledBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить задания в статусе Executing, которые висят дольше указанного времени.
    /// Используется для механизма Heartbeat — обнаружения зависших задач.
    /// </summary>
    // Task<IReadOnlyList<ScheduledTask>> GetExecutingOlderThanAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Обновить задание с контролем оптимистичной блокировки.
    /// Если версия задания в БД не совпадает с expectedVersion, 
    /// реализация должна выбросить ConcurrencyException.
    /// </summary>
    /// <param name="task">Агрегат с изменённым состоянием.</param>
    /// <param name="expectedVersion">Версия, которую ожидает клиент (та, что была при загрузке).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UpdateAsync(ScheduledTask task, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает список заданий в статусе Executing, у которых LockedUntil <= utcNow.
    /// </summary>
    Task<IReadOnlyList<ScheduledTask>> GetStaleExecutingTasksAsync(DateTime utcNow, CancellationToken ct);
    /// <summary>
    /// Атомарно захватить следующее задание из очереди (статус Queued).
    /// Гарантирует, что одно задание не попадёт двум воркерам одновременно.
    /// Реализуется через SELECT FOR UPDATE SKIP LOCKED.
    /// </summary>
    Task<ScheduledTask?> AcquireNextQueuedAsync( CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Атомарно захватывает задание по идентификатору, если оно ещё в статусе Queued.
    /// Устанавливает статус Executing и LockedUntil = utcNow + таймаут + буфер.
    /// Возвращает задание или null, если захват не удался.
    /// </summary>
    /// <param name="taskId">Идентификатор задания.</param>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="timeoutSeconds">Таймаут выполнения в секундах (из стратегии), может быть null.</param>
    /// <param name="ct">Токен отмены.</param>
    Task<ScheduledTask?> TryAcquireQueuedTaskAsync(
        TaskId taskId, DateTime utcNow, int? timeoutSeconds, CancellationToken ct);
    
    /// <summary>
    /// Пакетное обновление заданий (статус, время блокировки и т.д.) в одной транзакции.
    /// Реализация использует UNNEST или несколько команд в одном соединении.
    /// </summary>
    /// <param name="tasks">Коллекция заданий с изменённым состоянием.</param>
    /// <param name="ct">Токен отмены.</param>
    Task BulkUpdateAsync(IReadOnlyCollection<ScheduledTask> tasks, 
        CancellationToken ct = default);
    
    
    /// <summary>
    /// Найти задание по ключу идемпотентности.
    /// Используется для предотвращения дубликатов при повторных запросах создания.
    /// </summary>
    /// <param name="idempotencyKey">Ключ идемпотентности.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задание или null, если не найдено.</returns>
    Task<ScheduledTask?> GetByIdempotencyKeyAsync(string idempotencyKey,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Получить список polling-заданий, готовых к проверке.
    /// </summary>
    /// <param name="utcNow">Текущее время для сравнения с NextExecutionAt.</param>
    /// <param name="batchSize">Максимальное количество задач в пакете.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<ScheduledTask>> GetScheduledPollingTasksAsync(
        DateTime utcNow, int batchSize, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно захватить polling-задание для выполнения:
    /// переводит из Scheduled в Executing и устанавливает LockedUntil,
    /// если задание с указанным Id имеет статус Scheduled и NextExecutionAt <= utcNow.
    /// Возвращает обновлённый агрегат или null, если захват не удался.
    /// </summary>
    Task<ScheduledTask?> TryAcquirePollingTaskAsync(
        TaskId taskId, DateTime utcNow, TimeSpan lockDuration, 
        CancellationToken cancellationToken = default);
}
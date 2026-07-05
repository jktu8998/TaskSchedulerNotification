using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using TaskStatus = Domain.Enums.TaskStatus;

namespace Domain.Interfaces;

/// <summary>
/// Контракт (порт) для доступа к хранилищу заданий.
/// Реализуется в слое Infrastructure.
/// </summary>
public interface ITaskRepository
{
    /// <summary>Добавить новое задание в хранилище.</summary>
    Task AddAsync(ScheduledTask task);

    /// <summary>Получить задание по идентификатору. Возвращает null, если не найдено.</summary>
    Task<ScheduledTask?> GetByIdAsync(TaskId id);

    /// <summary>
    /// Получить задания, созданные конкретным сервисом-отправителем.
    /// Поддерживает фильтрацию по статусу и типу, пагинацию.
    /// </summary>
    Task<IReadOnlyList<ScheduledTask>> GetBySenderIdAsync(
        string senderId, int skip, int take, TaskStatus? status = null, TaskType? type = null);

    /// <summary>
    /// Получить задания, у которых статус Scheduled и время выполнения наступило.
    /// Используется планировщиком для отправки в очередь.
    /// </summary>
    Task<IReadOnlyList<ScheduledTask>> GetScheduledBeforeAsync(DateTime cutoff);

    /// <summary>
    /// Получить задания в статусе Executing, которые висят дольше указанного времени.
    /// Используется для механизма Heartbeat — обнаружения зависших задач.
    /// </summary>
    Task<IReadOnlyList<ScheduledTask>> GetExecutingOlderThanAsync(TimeSpan timeout);

    /// <summary>Обновить существующее задание (статус, свойства).</summary>
    Task UpdateAsync(ScheduledTask task);

    /// <summary>
    /// Атомарно захватить следующее задание из очереди (статус Queued).
    /// Гарантирует, что одно задание не попадёт двум воркерам одновременно.
    /// Реализуется через SELECT FOR UPDATE SKIP LOCKED.
    /// </summary>
    Task<ScheduledTask?> AcquireNextQueuedAsync();
}
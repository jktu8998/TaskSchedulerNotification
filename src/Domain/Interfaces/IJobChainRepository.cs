using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Interfaces;

/// <summary>
/// Контракт (порт) для доступа к хранилищу цепочек заданий.
/// Реализуется в слое Infrastructure.
/// </summary>
public interface IJobChainRepository
{
    /// <summary>Добавить новую цепочку заданий.</summary>
    Task AddAsync(JobChain chain, CancellationToken cancellationToken = default);

    /// <summary>Получить цепочку по идентификатору. Возвращает null, если не найдено.</summary>
    Task<JobChain?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить цепочки, созданные конкретным сервисом-отправителем.
    /// Поддерживает фильтрацию по статусу и пагинацию.
    /// </summary>
    Task<IReadOnlyList<JobChain>> GetBySenderIdAsync(
        SenderId senderId, int skip, int take,
        ChainStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Обновить цепочку с контролем оптимистичной блокировки.
    /// Если версия в БД не совпадает с expectedVersion, выбрасывается ConcurrencyException.
    /// </summary>
    Task UpdateAsync(JobChain chain, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Найти активные цепочки, у которых текущее задание зависло (LockedUntil истекло).
    /// Используется механизмом Heartbeat для цепочек.
    /// </summary>
    Task<IReadOnlyList<JobChain>> GetStaleActiveChainsAsync(DateTime utcNow, CancellationToken ct = default);
}
using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Entities;

namespace Domain.Interfaces;

/// <summary>
/// Контракт для хранилища Dead Letter Queue.
/// Содержит задания, исчерпавшие все повторные попытки.
/// </summary>
public interface IDeadLetterRepository
{
    /// <summary>Добавить задание в DLQ.</summary>
    Task AddAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Получить запись DLQ по идентификатору записи.</summary>
    Task<DeadLetterEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Получить все записи DLQ с пагинацией.</summary>
    Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Удалить запись из DLQ (например, после ручного перезапуска).</summary>
    Task RemoveAsync(long id, CancellationToken cancellationToken = default);
}
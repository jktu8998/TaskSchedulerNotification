using System.Threading.Tasks;
using Domain.Entities;
using Domain.ValueObjects;

namespace Domain.Interfaces;

/// <summary>
/// Контракт для хранилища состояний polling-заданий.
/// Хранит последний ответ внешнего сервиса для сравнения "изменилось/не изменилось".
/// </summary>
public interface IPollingStateRepository
{
    /// <summary>Получить состояние polling-задания по его идентификатору.</summary>
    Task<PollingState?> GetByTaskIdAsync(TaskId taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Создать или обновить состояние polling-задания.
    /// Если записи ещё нет — создаёт, если есть — обновляет LastResponseJson и LastCheckedAt.
    /// </summary>
    Task UpsertAsync(PollingState state, CancellationToken cancellationToken = default);
}
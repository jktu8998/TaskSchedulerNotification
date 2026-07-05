using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Entities;
using Domain.ValueObjects;

namespace Domain.Interfaces;

/// <summary>
/// Контракт для хранилища логов заданий.
/// Логи пишутся при каждом изменении статуса или ошибке.
/// </summary>
public interface ITaskLogRepository
{
    /// <summary>Добавить новую запись в лог.</summary>
    Task AddAsync(TaskLog log);

    /// <summary>
    /// Получить все логи по идентификатору задания.
    /// Возвращает записи в хронологическом порядке.
    /// </summary>
    Task<IReadOnlyList<TaskLog>> GetByTaskIdAsync(TaskId taskId);
}
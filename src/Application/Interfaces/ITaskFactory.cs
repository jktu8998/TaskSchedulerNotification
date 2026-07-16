using Application.Dto;
using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Фабрика для создания агрегата ScheduledTask из входного DTO.
/// Инкапсулирует маппинг, шифрование, вычисление расписания и первоначальное планирование.
/// </summary>
public interface ITaskFactory
{
    /// <summary>
    /// Создаёт новый агрегат ScheduledTask, готовый к сохранению (статус Scheduled).
    /// </summary>
    /// <param name="request">DTO с данными от клиента.</param>
    /// <param name="senderId">Идентификатор сервиса-отправителя.</param>
    /// <param name="utcNow">Текущее время.</param>
    /// <returns>Новый агрегат с проставленным NextExecutionAt и статусом Scheduled.</returns>
    ScheduledTask CreateFromRequest(CreateTaskRequest request, 
        string senderId, DateTime utcNow,string idempotencyKey);
    /// <summary>
    /// Создаёт агрегат из снапшота DLQ, где sensitive-данные уже зашифрованы.
    /// </summary>
    ScheduledTask CreateFromSnapshot(TaskSnapshotDto snapshot,
        string senderId, DateTime utcNow);
}
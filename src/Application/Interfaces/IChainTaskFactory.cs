using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Interfaces;

/// <summary>
/// Фабрика для создания заданий, являющихся шагами цепочки.
/// Инкапсулирует построение CreateTaskRequest и вызов ITaskFactory.
/// </summary>
public interface IChainTaskFactory
{
    /// <summary>
    /// Создаёт ScheduledTask для указанного шага цепочки.
    /// </summary>
    /// <param name="step">Доменный объект шага цепочки.</param>
    /// <param name="chainId">Идентификатор цепочки.</param>
    /// <param name="senderId">Идентификатор сервиса-отправителя.</param>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="stepIndex">Индекс шага в цепочке.</param>
    /// <returns>Новый агрегат ScheduledTask, готовый к сохранению.</returns>
    ScheduledTask CreateTaskForStep(
        ChainStep step,
        TaskId chainId,
        string senderId,
        DateTime utcNow,
        int stepIndex);
}
using System;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Состояние polling-задания: хранит последний ответ от внешнего сервиса
/// и время последней проверки. Нужно для сравнения "изменилось/не изменилось".
/// </summary>
public sealed class PollingState
{
    public TaskId TaskId { get; private set; }
    public string? LastResponseJson { get; private set; }
    public DateTime? LastCheckedAt { get; private set; }

    public PollingState(TaskId taskId)
    {
        TaskId = taskId;
    }

    /// <summary>
    /// Обновляет состояние после проверки polling-задания.
    /// </summary>
    /// <param name="responseJson">Тело ответа (сохраняется для будущих сравнений).</param>
    /// <param name="utcNow">Время проверки.</param>
    public void UpdateState(string? responseJson, DateTime utcNow)
    {
        LastResponseJson = responseJson;
        LastCheckedAt = utcNow;
    }
}
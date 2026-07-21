namespace Domain.Enums;

/// <summary>
/// Действие, выполняемое, когда все повторные попытки шага исчерпаны и он перемещён в DLQ.
/// </summary>
public enum FailureAction
{
    /// <summary>Остановить всю цепочку (статус Failed).</summary>
    Stop = 0,

    /// <summary>Пропустить шаг и перейти к следующему.</summary>
    SkipToNext = 1,

    /// <summary>Перейти к указанному компенсирующему шагу.</summary>
    Compensate = 2
}
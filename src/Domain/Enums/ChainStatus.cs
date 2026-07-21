namespace Domain.Enums;

/// <summary>
/// Статусы жизненного цикла цепочки заданий.
/// </summary>
public enum ChainStatus
{
    /// <summary>Цепочка создана, но ещё не активна (ожидает запуска).</summary>
    Created = 0,

    /// <summary>Цепочка активна, шаги выполняются.</summary>
    Active = 1,

    /// <summary>Цепочка успешно завершена (все шаги выполнены).</summary>
    Completed = 2,

    /// <summary>Цепочка остановлена из-за ошибки (действие Stop при провале шага).</summary>
    Failed = 3,

    /// <summary>Цепочка приостановлена пользователем.</summary>
    Paused = 4,

    /// <summary>Цепочка отменена пользователем.</summary>
    Cancelled = 5
}
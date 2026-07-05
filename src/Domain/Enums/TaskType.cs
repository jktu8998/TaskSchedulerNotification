namespace Domain.Enums;

/// <summary>
/// Тип задания: определяет, как задание будет планироваться и выполняться.
/// </summary>
public enum TaskType
{
    /// <summary>Выполняется однократно в указанное время или через смещение.</summary>
    OneTime = 1,

    /// <summary>Выполняется многократно по расписанию (cron, интервал).</summary>
    Periodic = 2,

    /// <summary>Периодически опрашивает внешний сервис и отслеживает изменения данных.</summary>
    Polling = 3
}
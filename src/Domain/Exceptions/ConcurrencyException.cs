namespace Domain.Exceptions;

/// <summary>
/// Возникает при попытке сохранить изменения в агрегате,
/// если его версия в хранилище не совпадает с ожидаемой (оптимистичная блокировка).
/// </summary>
public sealed class ConcurrencyException : Exception
{
    /// <summary>
    /// Идентификатор задания, для которого произошёл конфликт версий.
    /// </summary>
    public Guid TaskId { get; }

    /// <summary>
    /// Версия, которая ожидалась клиентом.
    /// </summary>
    public int ExpectedVersion { get; }

    /// <summary>
    /// Фактическая версия в хранилище (если известна, иначе null).
    /// </summary>
    public int? ActualVersion { get; }

    public ConcurrencyException(Guid taskId, int expectedVersion, int? actualVersion = null)
        : base($"Concurrency conflict for task {taskId}. " +
               $"Expected version {expectedVersion}, actual version {actualVersion?.ToString() ?? "unknown"}.")
    {
        TaskId = taskId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}
namespace Application.Dto;

/// <summary>
/// Конфигурация HTTP-запроса для выполнения.
/// </summary>
public sealed record ExecutionConfigDto
{
    /// <summary>
    /// Тип стратегии выполнения. Определяет, как десериализовать параметры.
    /// По умолчанию "http" для обратной совместимости.
    /// </summary>
    public string ExecutionType { get; init; } = "http";
    public string Method { get; init; }
    public string Url { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? Body { get; init; }
    /// <summary>
    /// Таймаут HTTP-запроса в секундах. Если null, используется значение по умолчанию (30 секунд).
    /// </summary>
    public int? TimeoutSeconds { get; init; }
}

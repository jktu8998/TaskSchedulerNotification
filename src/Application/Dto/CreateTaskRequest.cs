
using System.Text.Json.Serialization;

namespace Application.Dto;

/// <summary>
/// DTO для создания нового задания. Принимается от Web-слоя.
/// Содержит примитивные типы и строки, которые Application смапит в доменные Value Objects.
/// </summary>
public sealed record CreateTaskRequest
{
    /// <summary>Тип задания: "OneTime", "Periodic" или "Polling".</summary>
    public string Type { get; init; }
    public ScheduleDto Schedule { get; init; }
    public ExecutionConfigDto Execution { get; init; }
    public ResultDeliveryConfigDto? ResultDelivery { get; init; }
    public PollingConfigDto? PollingConfig { get; init; }
    public RetryPolicyDto? Retry { get; init; }
    public string? SensitiveData { get; init; }
    /// <summary>
    /// Произвольные дополнительные поля, которые не предусмотрены явными свойствами.
    /// Автоматически заполняется System.Text.Json при десериализации.
    /// Все значения должны быть строками (для совместимости с TaskMetadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; init; }
}
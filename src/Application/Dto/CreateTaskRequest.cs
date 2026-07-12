
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
}
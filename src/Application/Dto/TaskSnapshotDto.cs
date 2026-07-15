namespace Application.Dto;

/// <summary>
/// Снапшот задания для сохранения в Dead Letter Queue.
/// Содержит все параметры, необходимые для повторного создания задания через ITaskFactory.
/// </summary>
public sealed record TaskSnapshotDto
{
    public string SenderId { get; init; }
    public string Type { get; init; }
    public ScheduleDto Schedule { get; init; }
    public ExecutionConfigDto Execution { get; init; }
    public ResultDeliveryConfigDto? ResultDelivery { get; init; }
    public PollingConfigDto? PollingConfig { get; init; }
    public RetryPolicyDto? Retry { get; init; }
    public string? EncryptedSensitiveData { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}
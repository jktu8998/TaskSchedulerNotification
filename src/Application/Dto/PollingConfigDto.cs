namespace Application.Dto;

/// <summary>
/// Конфигурация polling-задания.
/// </summary>
public sealed record PollingConfigDto
{
    public string Field { get; init; }
    public string? Condition { get; init; }
    public string? Value { get; init; }
    public int IntervalSeconds { get; init; } = 60;
    public bool VerboseLogging { get; init; }
}
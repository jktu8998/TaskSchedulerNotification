
namespace  Domain.ValueObjects;

/// <summary>
/// Конфигурация для polling-заданий. Неизменяемый тип, сравнивается по значению.
/// </summary>
public sealed record PollingConfig
{
    public string Field { get; init; }
    public string? Condition { get; init; }
    public string? Value { get; init; }
    public int IntervalSeconds { get; init; }
    public bool VerboseLogging { get; init; }

    public PollingConfig(string field, string? condition, string? value, int intervalSeconds = 60, bool verboseLogging = false)
    {
        Field = field;
        Condition = condition;
        Value = value;
        IntervalSeconds = intervalSeconds;
        VerboseLogging = verboseLogging;
    }
}
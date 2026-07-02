namespace  Domain.ValueObjects;

public sealed class PollingConfig
{
    public string Field { get; }
    public string? Condition { get; } // "changed", "greater_than", "not_equal", etc.
    public string? Value { get; }
    public int IntervalSeconds { get; }
    public bool VerboseLogging { get; }

    public PollingConfig(string field, string? condition, string? value, int intervalSeconds = 60, bool verboseLogging = false)
    {
        Field = field;
        Condition = condition;
        Value = value;
        IntervalSeconds = intervalSeconds;
        VerboseLogging = verboseLogging;
    }
}
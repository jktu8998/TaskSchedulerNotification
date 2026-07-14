
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
        // Проверка Field: обязательное, не пустое
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field cannot be null or empty.", nameof(field));

        // Если задано условие, должно быть и значение для сравнения
        if (!string.IsNullOrWhiteSpace(condition) && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "Value must be provided when a condition is specified.", nameof(value));

        // Интервал опроса должен быть положительным
        if (intervalSeconds <= 0)
            throw new ArgumentException(
                "Interval must be greater than 0.", nameof(intervalSeconds));

        Field = field;
        Condition = condition;
        Value = value;
        IntervalSeconds = intervalSeconds;
        VerboseLogging = verboseLogging;
    }
    /// <summary>
    /// Структурное сравнение всех полей (все строки сравниваются с Ordinal семантикой).
    /// </summary>
    public bool Equals(PollingConfig? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Field, other.Field, StringComparison.Ordinal)
               && string.Equals(Condition, other.Condition, StringComparison.Ordinal)
               && string.Equals(Value, other.Value, StringComparison.Ordinal)
               && IntervalSeconds == other.IntervalSeconds
               && VerboseLogging == other.VerboseLogging;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Field, StringComparer.Ordinal);
        hash.Add(Condition, StringComparer.Ordinal);
        hash.Add(Value, StringComparer.Ordinal);
        hash.Add(IntervalSeconds);
        hash.Add(VerboseLogging);
        return hash.ToHashCode();
    }
}
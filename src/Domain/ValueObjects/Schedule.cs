namespace Domain.ValueObjects;

public sealed class Schedule
{
    public DateTimeOffset? ExecuteAt { get; }
    public TimeSpan? Offset { get; }
    public string? CronExpression { get; }
    public string? Timezone { get; } // IANA, e.g., "Europe/Moscow"

    private Schedule(DateTimeOffset? executeAt, TimeSpan? offset, string? cron, string? timezone)
    {
        ExecuteAt = executeAt;
        Offset = offset;
        CronExpression = cron;
        Timezone = timezone;
    }

    public static Schedule FromAbsolute(DateTimeOffset executeAt)
        => new(executeAt, null, null, null);

    public static Schedule FromOffset(TimeSpan offset)
        => new(null, offset, null, null);

    public static Schedule FromCron(string cronExpression, string timezone)
        => new(null, null, cronExpression, timezone);

    public bool IsAbsolute => ExecuteAt.HasValue;
    public bool IsOffset => Offset.HasValue;
    public bool IsCron => !string.IsNullOrWhiteSpace(CronExpression);

    public override string ToString() =>
        IsAbsolute ? $"At {ExecuteAt:O}" :
        IsOffset ? $"After {Offset}" :
        IsCron ? $"Cron '{CronExpression}' ({Timezone})" : "Unknown";
}
using System.Collections.Generic;
using System.Linq;

namespace  Domain.ValueObjects;

public sealed class RetryPolicy
{
    public IReadOnlyList<int> IntervalsSeconds { get; } // например [60, 120, 300]
    public int MaxAttempts => IntervalsSeconds.Count;

    public RetryPolicy(IReadOnlyList<int> intervalsSeconds)
    {
        IntervalsSeconds = intervalsSeconds;
    }

    public static RetryPolicy Default => new(new[] { 60, 60, 60, 60, 60 }); // 5 попыток через минуту
}
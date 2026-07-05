using System.Collections.Generic;
using System.Linq;

namespace Domain.ValueObjects;

/// <summary>
/// Политика повторных попыток выполнения задания.
/// Хранит интервалы в секундах между последовательными попытками.
/// Количество попыток = длине массива интервалов.
/// Неизменяемый, сравнивается по значению.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>
    /// Массив интервалов в секундах. Например, [60, 120, 300] — 
    /// первая повторная попытка через 60с, вторая через 120с, третья через 300с.
    /// </summary>
    public IReadOnlyList<int> IntervalsSeconds { get; init; }

    /// <summary>
    /// Максимальное количество попыток (включая первую).
    /// </summary>
    public int MaxAttempts => IntervalsSeconds.Count;

    public RetryPolicy(IReadOnlyList<int> intervalsSeconds)
    {
        IntervalsSeconds = intervalsSeconds;
    }

    /// <summary>
    /// Политика по умолчанию: 5 попыток с интервалом 60 секунд.
    /// </summary>
    public static RetryPolicy Default => new(new[] { 60, 60, 60, 60, 60 });
}
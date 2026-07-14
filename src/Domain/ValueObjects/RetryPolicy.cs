
using System.Collections.Immutable;

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
    public ImmutableArray<int> IntervalsSeconds { get; init; }

    /// <summary>
    /// Максимальное количество попыток (включая первую).
    /// </summary>
    public int MaxAttempts => IntervalsSeconds.Length;

    

    /// <summary>
    /// Политика по умолчанию: 5 попыток с интервалом 60 секунд.
    /// </summary>
    public static RetryPolicy Default => new(new[] { 60, 60, 60, 60, 60 });
    
    public RetryPolicy(IReadOnlyList<int> intervalsSeconds)
    {
        // 1. Null check
        if (intervalsSeconds is null)
            throw new ArgumentNullException(nameof(intervalsSeconds));

        // 2. Хотя бы одна попытка
        if (intervalsSeconds.Count == 0)
            throw new ArgumentException("At least one retry interval must be specified.", nameof(intervalsSeconds));

        // 3. Все интервалы > 0
        for (int i = 0; i < intervalsSeconds.Count; i++)
        {
            if (intervalsSeconds[i] <= 0)
                throw new ArgumentException(
                    $"All retry intervals must be greater than 0. Index {i} has value {intervalsSeconds[i]}.",
                    nameof(intervalsSeconds));
        }

        // Защита от мутации: копируем в ImmutableArray
        IntervalsSeconds = intervalsSeconds.ToImmutableArray();
    }
    /// <summary>
    /// Реализация IEquatable&lt;RetryPolicy&gt; — структурное сравнение интервалов.
    /// </summary>
    public bool Equals(RetryPolicy? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return IntervalsSeconds.SequenceEqual(other.IntervalsSeconds);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var interval in IntervalsSeconds)
            hash.Add(interval);
        return hash.ToHashCode();
    }
}
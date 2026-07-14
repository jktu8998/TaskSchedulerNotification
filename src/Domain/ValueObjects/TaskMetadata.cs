using System.Collections.Immutable;

namespace Domain.ValueObjects;

/// <summary>
/// Произвольные метаданные задания (Property Bag).
/// Неизменяемый словарь ключ-значение, сравниваемый по содержимому.
/// </summary>
public sealed record TaskMetadata
{
    public static readonly TaskMetadata Empty = new(new Dictionary<string, string>());

    public ImmutableDictionary<string, string> Data { get; init; }

    public TaskMetadata(IReadOnlyDictionary<string, string> data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        if (data.Count > 64)
            throw new ArgumentException("Metadata cannot exceed 64 key-value pairs.", nameof(data));

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var kvp in data)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
                throw new ArgumentException("Metadata keys cannot be null or empty.", nameof(data));
            if (kvp.Key.Length > 128)
                throw new ArgumentException($"Metadata key exceeds 128 characters: '{kvp.Key}'.", nameof(data));
            if (kvp.Value is null)
                throw new ArgumentException($"Metadata value for key '{kvp.Key}' cannot be null.", nameof(data));
            if (kvp.Value.Length > 1024)
                throw new ArgumentException($"Metadata value for key '{kvp.Key}' exceeds 1024 characters.", nameof(data));

            builder.Add(kvp.Key, kvp.Value);
        }

        Data = builder.ToImmutable();
    }

    // Структурное равенство
    public bool Equals(TaskMetadata? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        // Быстрая проверка по количеству
        if (Data.Count != other.Data.Count) return false;

        // Полное сравнение пар
        foreach (var kvp in Data)
        {
            if (!other.Data.TryGetValue(kvp.Key, out var otherValue) ||
                !string.Equals(kvp.Value, otherValue, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        // Сортируем ключи для детерминированного хеша
        foreach (var key in Data.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(Data[key], StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}
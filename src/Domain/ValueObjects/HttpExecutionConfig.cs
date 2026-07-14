using System.Collections.Immutable;

namespace Domain.ValueObjects;

/// <summary>
/// Стратегия выполнения HTTP-запроса.
/// Сравнивается структурно, включая заголовки и тело.
/// </summary>
public sealed record HttpExecutionConfig : ExecutionStrategy
{
    public override string StrategyType => "http";

    public string Method { get; init; }
    public string Url { get; init; }
    public ImmutableDictionary<string, string> Headers { get; init; } = ImmutableDictionary<string, string>.Empty;
    public string? Body { get; init; }

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"
    };

    public HttpExecutionConfig(
        string method,
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        string? body = null,
        int? timeoutSeconds = null)
        : base(timeoutSeconds) // вызов валидации таймаута в базовом классе
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("HTTP method cannot be null or empty.", nameof(method));
        if (!AllowedMethods.Contains(method))
            throw new ArgumentException($"HTTP method '{method}' is not allowed.", nameof(method));

        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException($"URL '{url}' is not a valid absolute HTTP/HTTPS URL.", nameof(url));

        var headerDict = headers ?? new Dictionary<string, string>();
        foreach (var kvp in headerDict)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
                throw new ArgumentException("Header keys cannot be null or empty.", nameof(headers));
        }

        Method = method.ToUpperInvariant();
        Url = url;
        Headers = headerDict.ToImmutableDictionary(StringComparer.Ordinal);
        Body = body;

        // Body должен быть валидным JSON, если не null
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(body);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ArgumentException("Body must be a valid JSON string.", nameof(body), ex);
            }
        }
    }

    // Пустой конструктор для Dapper/десериализации
    private HttpExecutionConfig() : base() { }

    /// <summary>
    /// Сравнение с учётом заголовков и тела.
    /// </summary>
    public bool Equals(HttpExecutionConfig? other)
    {
        if (other is null) return false;
        if (!base.Equals(other)) return false;

        return string.Equals(Method, other.Method, StringComparison.Ordinal)
               && string.Equals(Url, other.Url, StringComparison.Ordinal)
               && Headers.Count == other.Headers.Count
               && Headers.All(kvp =>
                   other.Headers.TryGetValue(kvp.Key, out var otherValue) &&
                   string.Equals(kvp.Value, otherValue, StringComparison.Ordinal))
               && string.Equals(Body, other.Body, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method, StringComparer.Ordinal);
        hash.Add(Url, StringComparer.Ordinal);
        foreach (var kvp in Headers.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key, StringComparer.Ordinal);
            hash.Add(kvp.Value, StringComparer.Ordinal);
        }
        hash.Add(Body, StringComparer.Ordinal);
        hash.Add(TimeoutSeconds); // из базового
        // StrategyType не добавляем, т.к. он фиксирован "http", но для надёжности:
        hash.Add(StrategyType, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
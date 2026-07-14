
using System.Collections.Immutable;

namespace  Domain.ValueObjects;

/// <summary>
/// Конфигурация HTTP-запроса для выполнения задания.
/// Неизменяемый, сравнивается по значению всех полей.
/// </summary>
public sealed record ExecutionConfig
{
    // Допустимые HTTP-методы
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"
    };
    public string Method { get; init; }
    public string Url { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
    public string? Body { get; init; }
    /// <summary>
    /// Таймаут HTTP-запроса в секундах. Если null, используется значение по умолчанию (30 секунд).
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Конструктор для создания конфига.
    /// Все поля обязательны, кроме Body и TimeoutSeconds.
    /// </summary>
    public ExecutionConfig(string method, string url,
        IReadOnlyDictionary<string, string>? headers = null, 
        string? body = null, int? timeoutSeconds = null)
    {
        // Проверка Method
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("HTTP method cannot be null or empty.", nameof(method));

        if (!AllowedMethods.Contains(method))
            throw new ArgumentException($"HTTP method '{method}' is not allowed. Allowed methods: {string.Join(", ", AllowedMethods)}.", nameof(method));

        // Проверка Url
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException($"URL '{url}' is not a valid absolute HTTP/HTTPS URL.", nameof(url));

        // Проверка Headers
        var headerDict = headers ?? new Dictionary<string, string>();
        foreach (var kvp in headerDict)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
                throw new ArgumentException("Header keys cannot be null or empty.", nameof(headers));
        }

        // Проверка TimeoutSeconds
        if (timeoutSeconds.HasValue && timeoutSeconds.Value <= 0)
            throw new ArgumentException("Timeout must be greater than 0.", nameof(timeoutSeconds));

        Method = method.ToUpperInvariant();
        Url = url;
        Headers = headerDict.ToImmutableDictionary(StringComparer.Ordinal); // копия с Ordinal сравнением ключей
        Body = body;
        TimeoutSeconds = timeoutSeconds;
    }
    // ===== Структурное равенство коллекций =====
    /// <summary>
    /// Реализация IEquatable&lt;ExecutionConfig&gt; для структурного сравнения (включая словарь).
    /// Компилятор не генерирует свой метод Equals(ExecutionConfig?), потому что этот уже есть.
    /// </summary>
    public bool Equals(ExecutionConfig? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Method, other.Method, StringComparison.Ordinal)
               && string.Equals(Url, other.Url, StringComparison.Ordinal)
               && Headers.Count == other.Headers.Count
               && Headers.All(kvp =>
                   other.Headers.TryGetValue(kvp.Key, out var otherValue) &&
                   string.Equals(kvp.Value, otherValue, StringComparison.Ordinal))
               && string.Equals(Body, other.Body, StringComparison.Ordinal)
               && TimeoutSeconds == other.TimeoutSeconds;
    }

    // public override bool Equals(object? obj) => Equals(obj as ExecutionConfig);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method, StringComparer.Ordinal);
        hash.Add(Url, StringComparer.Ordinal);
        // Для словаря комбинируем хеши всех пар
        foreach (var kvp in Headers.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key, StringComparer.Ordinal);
            hash.Add(kvp.Value, StringComparer.Ordinal);
        }
        hash.Add(Body, StringComparer.Ordinal);
        hash.Add(TimeoutSeconds);
        return hash.ToHashCode();
    }
}
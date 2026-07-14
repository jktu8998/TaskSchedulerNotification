using System.Text.Json;
using Domain.Enums;

namespace Domain.ValueObjects;

/// <summary>
/// Конфигурация доставки результата выполнения задания.
/// Определяет, куда и как отправить результат после успешного выполнения.
/// Неизменяемый, сравнивается по значению.
/// </summary>
public sealed record ResultDeliveryConfig
{
    
    // Допустимые HTTP-методы для доставки
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"
    };
    /// <summary>
    /// Режим доставки: переслать ответ как есть (ForwardResponse) 
    /// или выполнить фиксированный вызов с параметрами (FixedCall).
    /// </summary>
    public ResultDeliveryMode Mode { get; init; }

    /// <summary>
    /// URL, на который доставляется результат.
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    /// HTTP-метод для доставки (POST, PUT и т.д.).
    /// </summary>
    public string Method { get; init; }

    /// <summary>
    /// Параметры запроса в формате JSON. Используется только для режима FixedCall.
    /// </summary>
    public string? Params { get; init; }

    public ResultDeliveryConfig(ResultDeliveryMode mode, string url, string method, string? @params = null)
    {
        // 1. Проверка Mode: должен быть валидным значением enum
        if (!Enum.IsDefined(mode))
            throw new ArgumentException($"Invalid delivery mode: {mode}.", nameof(mode));

        // 2. Проверка Url: обязательный, абсолютный, HTTP/HTTPS
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException(
                $"URL '{url}' is not a valid absolute HTTP/HTTPS URL.", nameof(url));

        // 3. Проверка Method: обязательный, входит в список допустимых HTTP-методов
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("HTTP method cannot be null or empty.", nameof(method));

        if (!AllowedMethods.Contains(method))
            throw new ArgumentException(
                $"HTTP method '{method}' is not allowed. Allowed methods: {string.Join(", ", AllowedMethods)}.", nameof(method));

        // 4. Проверка Params в зависимости от Mode
        if (mode == ResultDeliveryMode.FixedCall && string.IsNullOrWhiteSpace(@params))
            throw new ArgumentException(
                "Params is required when Mode is FixedCall.", nameof(@params));

        // 5. Если Params задан, проверяем, что это валидный JSON
        if (!string.IsNullOrWhiteSpace(@params))
        {
            try
            {
                JsonDocument.Parse(@params);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(
                    "Params must be a valid JSON string.", nameof(@params), ex);
            }
        }

        Mode = mode;
        Url = url;
        Method = method.ToUpperInvariant();
        Params = @params;
    }
    /// <summary>
    /// Структурное сравнение всех полей.
    /// </summary>
    public bool Equals(ResultDeliveryConfig? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Mode == other.Mode
               && string.Equals(Url, other.Url, StringComparison.Ordinal)
               && string.Equals(Method, other.Method, StringComparison.Ordinal)
               && string.Equals(Params, other.Params, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);
        hash.Add(Url, StringComparer.Ordinal);
        hash.Add(Method, StringComparer.Ordinal);
        hash.Add(Params, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
using System.Collections.Generic;

namespace  Domain.ValueObjects;

/// <summary>
/// Конфигурация HTTP-запроса для выполнения задания.
/// Неизменяемый, сравнивается по значению всех полей.
/// </summary>
public sealed record ExecutionConfig
{
    public string Method { get; init; }
    public string Url { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
    public string? Body { get; init; }

    /// <summary>
    /// Конструктор для создания конфига.
    /// Все поля обязательны, кроме Body.
    /// </summary>
    public ExecutionConfig(string method, string url,
        IReadOnlyDictionary<string, string>? headers = null, string? body = null)
    {
        Method = method.ToUpperInvariant();
        Url = url;
        Headers = headers ?? new Dictionary<string, string>();
        Body = body;
    }
}
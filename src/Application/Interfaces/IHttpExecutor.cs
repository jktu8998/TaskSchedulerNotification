using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces;

/// <summary>
/// Конфигурация HTTP-запроса для выполнения.
/// </summary>
public sealed record HttpRequestConfig(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string>? Headers,
    string? Body)
{
    /// <summary>
    /// Таймаут выполнения запроса. Если null, используется значение по умолчанию.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// Результат выполнения HTTP-запроса.
/// </summary>
public sealed record HttpResponseResult(
    int StatusCode,
    string? Body,
    bool IsSuccess);

/// <summary>
/// Абстракция над HTTP-клиентом для выполнения запросов к внешним сервисам.
/// </summary>
public interface IHttpExecutor
{
    /// <summary>
    /// Выполнить HTTP-запрос. Принимает CancellationToken для отмены (graceful shutdown).
    /// </summary>
    Task<HttpResponseResult> ExecuteAsync(HttpRequestConfig config, CancellationToken cancellationToken = default);}
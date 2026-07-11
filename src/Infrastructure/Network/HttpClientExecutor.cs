using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces;

namespace Infrastructure.Network;

/// <summary>
/// Реализация IHttpExecutor на основе IHttpClientFactory.
/// Выполняет HTTP-запросы к внешним сервисам, обрабатывает таймауты и ошибки без проброса исключений.
/// </summary>
public sealed class HttpClientExecutor : IHttpExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpClientExecutor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HttpResponseResult> ExecuteAsync(HttpRequestConfig config, CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient("TaskExecutor");

        // HttpRequestMessage реализует IDisposable, оборачиваем в using
        using var request = new HttpRequestMessage
        {
            Method = new HttpMethod(config.Method),
            RequestUri = new Uri(config.Url),
            Content = config.Body != null
                ? new StringContent(config.Body, Encoding.UTF8, "application/json")
                : null
        };

        if (config.Headers != null)
        {
            foreach (var header in config.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;

        try
        {
            CancellationToken effectiveToken = cancellationToken;

            // Настраиваем комбинированный таймаут
            if (config.Timeout.HasValue && config.Timeout.Value > TimeSpan.Zero)
            {
                timeoutCts = new CancellationTokenSource(config.Timeout.Value);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                effectiveToken = linkedCts.Token;
            }

            var response = await httpClient.SendAsync(request, effectiveToken);
            var body = await response.Content.ReadAsStringAsync(effectiveToken);

            return new HttpResponseResult(
                (int)response.StatusCode,
                body,
                response.IsSuccessStatusCode
            );
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Сработал именно наш таймаут (timeoutCts), а не глобальная остановка сервиса
            return new HttpResponseResult(0, "Request timed out", false);
        }
        catch (HttpRequestException ex)
        {
            // Ошибка на уровне сети (DNS, нет маршрута, Connection Refused)
            return new HttpResponseResult(0, ex.Message, false);
        }
        catch (Exception ex)
        {
            // Глобальный fallback для непредвиденных сбоев (например, невалидный URI)
            // Возвращаем IsSuccess = false, чтобы воркер не упал с концами
            return new HttpResponseResult(0, $"Unexpected error: {ex.Message}", false);
        }
        finally
        {
            // Критично: очищаем оба источника токенов для предотвращения утечек памяти
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }
}
using System.Net;
using System.Text.Json;
using Domain.Exceptions;
using FluentValidation;

namespace WebLayer.Middleware;

/// <summary>
/// Middleware для глобальной обработки исключений.
/// Логирует ошибку и возвращает единообразный JSON-ответ.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Необработанное исключение при обработке запроса {Method} {Path}", context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            ValidationException validationEx => (HttpStatusCode.BadRequest, FormatValidationErrors(validationEx)),
            ConcurrencyException => (HttpStatusCode.Conflict, "Resource has been modified by another request. Please reload and try again."),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Access denied."),
            InvalidOperationException => (HttpStatusCode.BadRequest, exception.Message),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new { error = message };
        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }

    private static string FormatValidationErrors(ValidationException exception)
    {
        var errors = exception.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}");
        return string.Join("; ", errors);
    }
}
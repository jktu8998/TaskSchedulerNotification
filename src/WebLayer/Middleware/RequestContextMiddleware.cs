using Microsoft.AspNetCore.Authorization;

namespace WebLayer.Middleware;

using System.Security.Claims;



/// <summary>
/// Middleware, извлекающий из аутентифицированного запроса SenderId и флаг администратора,
/// и сохраняющий их в HttpContext.Items для дальнейшего использования в RequestContext.
/// </summary>
public sealed class RequestContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestContextMiddleware> _logger;

    public RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Разрешаем анонимный доступ к эндпоинтам аутентификации
        if (context.Request.Path.StartsWithSegments("/api/v1/Auth", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }
        // Проверка аутентификации
        if (context.User.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("Request without authentication");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Authentication required\"}");
            return; // <-- ПРЕРЫВАЕМ конвейер
        }

        // Извлечение SenderId
        var senderId = context.User.FindFirstValue("sender_id")
                       ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(senderId))
        {
            _logger.LogWarning("Authenticated user without sender_id claim");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"SenderId claim is missing\"}");
            return; // <-- ПРЕРЫВАЕМ конвейер
        }

        // Сохраняем в Items для RequestContext
        context.Items["SenderId"] = senderId;
        context.Items["IsAdmin"] = context.User.HasClaim(c => c.Type == "admin" && c.Value == "true")
                                   || context.User.IsInRole("Admin");

        await _next(context); // передаём управление дальше только при успехе
    }
}
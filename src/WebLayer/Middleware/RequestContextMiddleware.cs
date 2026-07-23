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

    public RequestContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Заполняем контекст только если пользователь аутентифицирован
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var senderId = context.User.FindFirstValue("sender_id")
                           ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrWhiteSpace(senderId))
            {
                context.Items["SenderId"] = senderId;
                context.Items["IsAdmin"] = context.User.HasClaim(c => c.Type == "admin" && c.Value == "true")
                                           || context.User.IsInRole("Admin");
            }
            // Если sender_id отсутствует, просто не заполняем – MVC-авторизация дальше решит, можно ли доступ
        }
        // Если не аутентифицирован – ничего не заполняем, но не блокируем

        await _next(context);
    }
}
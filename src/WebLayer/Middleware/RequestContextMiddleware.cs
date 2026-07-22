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
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // Извлекаем SenderId из claim'а "sender_id" (или "sub" как fallback)
            var senderId = context.User.FindFirstValue("sender_id")
                           ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrWhiteSpace(senderId))
            {
                context.Items["SenderId"] = senderId;

                // Проверяем, является ли отправитель администратором (claim "admin" или роль "Admin")
                var isAdmin = context.User.HasClaim(c => c.Type == "admin" && c.Value == "true")
                              || context.User.IsInRole("Admin");
                context.Items["IsAdmin"] = isAdmin;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("{\"error\":\"SenderId claim is missing\"}");
                return;
            }
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("{\"error\":\"Authentication required\"}");
            return;
        }

        await _next(context);
    }
}
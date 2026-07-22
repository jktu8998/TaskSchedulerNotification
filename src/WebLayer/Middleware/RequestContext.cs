namespace WebLayer.Middleware;

using Application.Interfaces;



/// <summary>
/// Контекст текущего запроса, извлекающий SenderId и флаг администратора из HTTP-контекста.
/// Заполняется middleware'ом RequestContextMiddleware.
/// </summary>
public sealed class RequestContext : IRequestContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string SenderId => _httpContextAccessor.HttpContext?.Items["SenderId"] as string
                              ?? throw new InvalidOperationException("SenderId not found in request context. Ensure RequestContextMiddleware is registered.");

    public bool IsAdmin => _httpContextAccessor.HttpContext?.Items["IsAdmin"] as bool? ?? false;
}
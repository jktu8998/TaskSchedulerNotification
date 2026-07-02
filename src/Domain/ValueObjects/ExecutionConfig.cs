using System.Collections.Generic;

namespace  Domain.ValueObjects;

public sealed class ExecutionConfig
{
    public string Method { get; } // GET, POST, PUT, PATCH, DELETE
    public string Url { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public string? Body { get; } // JSON string

    public ExecutionConfig(string method, string url,
        IReadOnlyDictionary<string, string>? headers = null, string? body = null)
    {
        Method = method.ToUpperInvariant();
        Url = url;
        Headers = headers ?? new Dictionary<string, string>();
        Body = body;
    }
}
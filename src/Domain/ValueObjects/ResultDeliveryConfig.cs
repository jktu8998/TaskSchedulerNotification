using  Domain.Enums;

namespace  Domain.ValueObjects;

public sealed class ResultDeliveryConfig
{
    public ResultDeliveryMode Mode { get; }
    public string Url { get; }
    public string Method { get; }
    public string? Params { get; } // JSON for FixedCall

    public ResultDeliveryConfig(ResultDeliveryMode mode, string url, string method, string? @params = null)
    {
        Mode = mode;
        Url = url;
        Method = method;
        Params = @params;
    }
}
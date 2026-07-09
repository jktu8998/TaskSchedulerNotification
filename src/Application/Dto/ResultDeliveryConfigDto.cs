namespace Application.Dto;

/// <summary>
/// Конфигурация доставки результата.
/// </summary>
public sealed record ResultDeliveryConfigDto
{
    public string Mode { get; init; }   // "ForwardResponse" или "FixedCall"
    public string Url { get; init; }
    public string Method { get; init; }
    public string? Params { get; init; }
}
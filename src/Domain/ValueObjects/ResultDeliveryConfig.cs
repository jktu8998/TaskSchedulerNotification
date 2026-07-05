using Domain.Enums;

namespace Domain.ValueObjects;

/// <summary>
/// Конфигурация доставки результата выполнения задания.
/// Определяет, куда и как отправить результат после успешного выполнения.
/// Неизменяемый, сравнивается по значению.
/// </summary>
public sealed record ResultDeliveryConfig
{
    /// <summary>
    /// Режим доставки: переслать ответ как есть (ForwardResponse) 
    /// или выполнить фиксированный вызов с параметрами (FixedCall).
    /// </summary>
    public ResultDeliveryMode Mode { get; init; }

    /// <summary>
    /// URL, на который доставляется результат.
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    /// HTTP-метод для доставки (POST, PUT и т.д.).
    /// </summary>
    public string Method { get; init; }

    /// <summary>
    /// Параметры запроса в формате JSON. Используется только для режима FixedCall.
    /// </summary>
    public string? Params { get; init; }

    public ResultDeliveryConfig(ResultDeliveryMode mode, string url, string method, string? @params = null)
    {
        Mode = mode;
        Url = url;
        Method = method;
        Params = @params;
    }
}
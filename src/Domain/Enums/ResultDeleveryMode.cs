namespace Domain.Enums;

/// <summary>
/// Режим доставки результата выполнения задания.
/// </summary>
public enum ResultDeliveryMode
{
    /// <summary>Переслать ответ от сервиса-исполнителя как есть.</summary>
    ForwardResponse = 1,

    /// <summary>Выполнить фиксированный вызов с заранее заданными параметрами.</summary>
    FixedCall = 2
}
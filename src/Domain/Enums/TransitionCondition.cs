namespace Domain.Enums;

/// <summary>
/// Условие, определяющее, следует ли переходить к следующему шагу после выполнения текущего.
/// </summary>
public enum TransitionCondition
{
    /// <summary>Всегда переходить (по умолчанию).</summary>
    Always = 0,

    /// <summary>Только при успешном выполнении (IsSuccess == true).</summary>
    OnSuccess = 1,

    /// <summary>Только при неудаче (IsSuccess == false).</summary>
    OnFailure = 2,

    /// <summary>Если HTTP-статус ответа равен заданному значению.</summary>
    IfStatusCode = 3,

    /// <summary>Если тело ответа содержит указанную подстроку.</summary>
    IfBodyContains = 4
}
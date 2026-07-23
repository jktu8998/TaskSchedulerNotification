
namespace Application.Dto;

/// <summary>
/// Упрощённое описание одного шага цепочки для передачи через API.
/// Содержит только примитивные типы и DTO, без доменных Value Objects.
/// </summary>
public sealed record ChainStepDto
{
    /// <summary>Индекс шага (0-based).</summary>
    public int StepIndex { get; init; }

    /// <summary>Расписание (может быть null — выполнить немедленно).</summary>
    public ScheduleDto? Schedule { get; init; }

    /// <summary>Конфигурация выполнения (обязательна).</summary>
    public ExecutionConfigDto Execution { get; init; }

    /// <summary>Доставка результата (опционально).</summary>
    public ResultDeliveryConfigDto? ResultDelivery { get; init; }

    /// <summary>Конфигурация polling (опционально).</summary>
    public PollingConfigDto? PollingConfig { get; init; }

    /// <summary>Политика повторов (если не задана — используется RetryPolicy.Default).</summary>
    public RetryPolicyDto? Retry { get; init; }

    /// <summary>Условие перехода к следующему шагу (строка-значение из TransitionCondition).</summary>
    public string TransitionCondition { get; init; } = "Always";

    /// <summary>Значение для условия (например, ожидаемый код ответа или подстрока).</summary>
    public string? ConditionValue { get; init; }

    /// <summary>Действие при окончательном провале шага (строка-значение из FailureAction).</summary>
    public string FailureAction { get; init; } = "Stop";

    /// <summary>Номер шага для компенсации (если FailureAction = Compensate).</summary>
    public int? CompensateStepIndex { get; init; }
}
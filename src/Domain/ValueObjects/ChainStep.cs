using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.ValueObjects;

/// <summary>
/// Описание одного шага в цепочке заданий.
/// Включает параметры выполнения, условия перехода к следующему шагу и действия при провале.
/// Неизменяемый, сравнивается структурно.
/// </summary>
public sealed record ChainStep
{
    /// <summary>Индекс шага (0-based), должен соответствовать позиции в массиве Steps агрегата JobChain.</summary>
    public int StepIndex { get; init; }

    /// <summary>
    /// Расписание для данного шага. Если null — задание будет выполнено немедленно (с нулевой задержкой).
    /// Для периодических или отложенных шагов можно задать конкретное расписание.
    /// </summary>
    public Schedule? Schedule { get; init; }

    /// <summary>Стратегия выполнения (HTTP, gRPC и т.п.). Обязательна.</summary>
    public ExecutionStrategy Execution { get; init; }

    /// <summary>Доставка результата (опционально).</summary>
    public ResultDeliveryConfig? ResultDelivery { get; init; }

    /// <summary>Конфигурация polling (если шаг polling-типа).</summary>
    public PollingConfig? PollingConfig { get; init; }

    /// <summary>Политика повторных попыток для этого шага (если не задана, используется RetryPolicy.Default).</summary>
    public RetryPolicy? RetryPolicy { get; init; }

    /// <summary>Условие перехода к следующему шагу.</summary>
    public TransitionCondition Condition { get; init; }

    /// <summary>Значение для условия (например, ожидаемый HTTP-код или подстрока). Используется для условий IfStatusCode, IfBodyContains.</summary>
    public string? ConditionValue { get; init; }

    /// <summary>Действие при окончательном провале шага (после исчерпания попыток).</summary>
    public FailureAction OnFailureAction { get; init; }

    /// <summary>Номер шага для компенсации (используется, если OnFailureAction == Compensate).</summary>
    public int? CompensateStepIndex { get; init; }

    public ChainStep(
        int stepIndex,
        ExecutionStrategy execution,
        Schedule? schedule = null,
        ResultDeliveryConfig? resultDelivery = null,
        PollingConfig? pollingConfig = null,
        RetryPolicy? retryPolicy = null,
        TransitionCondition condition = TransitionCondition.Always,
        string? conditionValue = null,
        FailureAction onFailureAction = FailureAction.Stop,
        int? compensateStepIndex = null)
    {
        if (stepIndex < 0)
            throw new ArgumentException("Step index cannot be negative.", nameof(stepIndex));
        if (execution is null)
            throw new ArgumentNullException(nameof(execution));
        if (!Enum.IsDefined(condition))
            throw new ArgumentException($"Invalid transition condition: {condition}", nameof(condition));
        if (!Enum.IsDefined(onFailureAction))
            throw new ArgumentException($"Invalid failure action: {onFailureAction}", nameof(onFailureAction));

        // Если условие требует значения, оно должно быть задано
        if (condition is TransitionCondition.IfStatusCode or TransitionCondition.IfBodyContains
            && string.IsNullOrWhiteSpace(conditionValue))
            throw new ArgumentException($"ConditionValue is required for condition '{condition}'.", nameof(conditionValue));

        // Если действие Compensate, должен быть указан индекс шага компенсации
        if (onFailureAction == FailureAction.Compensate && compensateStepIndex is null)
            throw new ArgumentException("CompensateStepIndex is required when OnFailureAction is Compensate.", nameof(compensateStepIndex));

        StepIndex = stepIndex;
        Schedule = schedule;
        Execution = execution;
        ResultDelivery = resultDelivery;
        PollingConfig = pollingConfig;
        RetryPolicy = retryPolicy;
        Condition = condition;
        ConditionValue = conditionValue;
        OnFailureAction = onFailureAction;
        CompensateStepIndex = compensateStepIndex;
    }

    // Пустой конструктор для десериализации/маппера
    private ChainStep() { }

    // Структурное сравнение
    public bool Equals(ChainStep? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return StepIndex == other.StepIndex
               && Equals(Schedule, other.Schedule)
               && Equals(Execution, other.Execution)
               && Equals(ResultDelivery, other.ResultDelivery)
               && Equals(PollingConfig, other.PollingConfig)
               && Equals(RetryPolicy, other.RetryPolicy)
               && Condition == other.Condition
               && string.Equals(ConditionValue, other.ConditionValue, StringComparison.Ordinal)
               && OnFailureAction == other.OnFailureAction
               && CompensateStepIndex == other.CompensateStepIndex;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StepIndex);
        hash.Add(Schedule);
        hash.Add(Execution);
        hash.Add(ResultDelivery);
        hash.Add(PollingConfig);
        hash.Add(RetryPolicy);
        hash.Add(Condition);
        hash.Add(ConditionValue, StringComparer.Ordinal);
        hash.Add(OnFailureAction);
        hash.Add(CompensateStepIndex);
        return hash.ToHashCode();
    }
}
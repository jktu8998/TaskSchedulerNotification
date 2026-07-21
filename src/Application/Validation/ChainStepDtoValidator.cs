using Application.Dto;
using Domain.Enums;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор одного шага цепочки заданий.
/// </summary>
public sealed class ChainStepDtoValidator : AbstractValidator<ChainStepDto>
{
    public ChainStepDtoValidator(
        ExecutionConfigDtoValidator executionValidator,
        ScheduleDtoValidator scheduleValidator)
    {
        // Индекс шага
        RuleFor(x => x.StepIndex)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Step index must be non-negative.");

        // Стратегия выполнения обязательна
        RuleFor(x => x.Execution)
            .NotNull()
            .WithMessage("Execution config is required.")
            .SetValidator(executionValidator!);

        // Если есть Schedule, валидируем
        When(x => x.Schedule != null, () =>
        {
            RuleFor(x => x.Schedule!)
                .SetValidator(scheduleValidator);
        });

        // Если есть ResultDelivery, проверяем обязательные поля
        When(x => x.ResultDelivery != null, () =>
        {
            RuleFor(x => x.ResultDelivery!.Mode)
                .NotEmpty().WithMessage("Delivery mode is required.");
            RuleFor(x => x.ResultDelivery!.Url)
                .NotEmpty().WithMessage("Delivery URL is required.");
            RuleFor(x => x.ResultDelivery!.Method)
                .NotEmpty().WithMessage("Delivery method is required.");
        });

        // TransitionCondition
        RuleFor(x => x.TransitionCondition)
            .NotEmpty()
            .Must(BeValidTransitionCondition)
            .WithMessage($"Transition condition must be one of: {string.Join(", ", Enum.GetNames<TransitionCondition>())}.");

        // Если условие требует значения
        When(x => BeConditionRequiringValue(x.TransitionCondition), () =>
        {
            RuleFor(x => x.ConditionValue)
                .NotEmpty()
                .WithMessage("ConditionValue is required for the selected transition condition.");
        });

        // FailureAction
        RuleFor(x => x.FailureAction)
            .NotEmpty()
            .Must(BeValidFailureAction)
            .WithMessage($"Failure action must be one of: {string.Join(", ", Enum.GetNames<FailureAction>())}.");

        // Если FailureAction == Compensate, нужен CompensateStepIndex
        When(x => IsCompensateAction(x.FailureAction), () =>
        {
            RuleFor(x => x.CompensateStepIndex)
                .NotNull()
                .GreaterThanOrEqualTo(0)
                .WithMessage("CompensateStepIndex is required and must be non-negative when FailureAction is Compensate.");
        });
    }

    private static bool BeValidTransitionCondition(string? value)
        => Enum.TryParse<TransitionCondition>(value, ignoreCase: true, out _);

    private static bool BeValidFailureAction(string? value)
        => Enum.TryParse<FailureAction>(value, ignoreCase: true, out _);

    private static bool BeConditionRequiringValue(string? condition)
        => Enum.TryParse<TransitionCondition>(condition, ignoreCase: true, out var parsed) &&
           (parsed == TransitionCondition.IfStatusCode || parsed == TransitionCondition.IfBodyContains);

    private static bool IsCompensateAction(string? action)
        => Enum.TryParse<FailureAction>(action, ignoreCase: true, out var parsed) &&
           parsed == FailureAction.Compensate;
}
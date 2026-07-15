using Application.Dto;
using Domain.Enums;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор DTO запроса на создание/обновление задания.
/// </summary>
public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator(
        ScheduleDtoValidator scheduleValidator,
        ExecutionConfigDtoValidator executionValidator)
    {
        // Тип задания
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Task type is required.")
            .Must(type => Enum.TryParse<TaskType>(type, ignoreCase: true, out _))
            .WithMessage($"Type must be one of: {string.Join(", ", Enum.GetNames<TaskType>())}.");

        // Расписание
        RuleFor(x => x.Schedule)
            .NotNull().WithMessage("Schedule is required.")
            .SetValidator(scheduleValidator!);

        // Конфигурация выполнения
        RuleFor(x => x.Execution)
            .NotNull().WithMessage("Execution config is required.")
            .SetValidator(executionValidator!);

        // Опциональная доставка результата
        When(x => x.ResultDelivery is not null, () =>
        {
            RuleFor(x => x.ResultDelivery!.Mode)
                .NotEmpty().WithMessage("Result delivery mode is required.")
                .Must(mode => Enum.TryParse<ResultDeliveryMode>(mode, ignoreCase: true, out _))
                .WithMessage($"Delivery mode must be one of: {string.Join(", ", Enum.GetNames<ResultDeliveryMode>())}.");
            RuleFor(x => x.ResultDelivery!.Url)
                .NotEmpty().WithMessage("Result delivery URL is required.");
            RuleFor(x => x.ResultDelivery!.Method)
                .NotEmpty().WithMessage("Result delivery method is required.");
        });

        // Опциональный polling-конфиг
        When(x => x.PollingConfig is not null, () =>
        {
            RuleFor(x => x.PollingConfig!.Field)
                .NotEmpty().WithMessage("Polling field is required.");
            RuleFor(x => x.PollingConfig!.IntervalSeconds)
                .GreaterThan(0).WithMessage("Polling interval must be greater than 0.");
        });

        // Опциональная политика повторов
        When(x => x.Retry?.IntervalsSeconds is { Length: > 0 }, () =>
        {
            RuleForEach(x => x.Retry!.IntervalsSeconds)
                .GreaterThan(0).WithMessage("Each retry interval must be greater than 0.");
        });
    }
}
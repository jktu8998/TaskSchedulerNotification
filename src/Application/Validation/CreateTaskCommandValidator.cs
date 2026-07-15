using Application.Commands;
using Domain.Enums;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор команды создания задания.
/// Проверяет вложенный DTO запроса целиком.
/// </summary>
public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator(
        ScheduleDtoValidator scheduleValidator,
        ExecutionConfigDtoValidator executionValidator)
    {
        RuleFor(x => x.Request)
            .NotNull()
            .WithMessage("Request body is required.");

        // Валидация вложенных DTO только если Request не null
        When(x => x.Request is not null, () =>
        {
            // Тип задания: обязателен, должен быть валидным TaskType
            RuleFor(x => x.Request.Type)
                .NotEmpty().WithMessage("Task type is required.")
                .Must(type => Enum.TryParse<TaskType>(type, ignoreCase: true, out _))
                .WithMessage($"Type must be one of: {string.Join(", ", Enum.GetNames<TaskType>())}.");

            // Расписание: обязательное, валидируется своим валидатором
            RuleFor(x => x.Request.Schedule)
                .NotNull().WithMessage("Schedule is required.")
                .SetValidator(scheduleValidator!);

            // Конфигурация выполнения: обязательна, валидируется полиморфным валидатором
            RuleFor(x => x.Request.Execution)
                .NotNull().WithMessage("Execution config is required.")
                .SetValidator(executionValidator!);

            // Опциональная доставка результата
            When(x => x.Request.ResultDelivery is not null, () =>
            {
                RuleFor(x => x.Request.ResultDelivery!.Mode)
                    .NotEmpty().WithMessage("Result delivery mode is required.")
                    .Must(mode => Enum.TryParse<ResultDeliveryMode>(mode, ignoreCase: true, out _))
                    .WithMessage($"Delivery mode must be one of: {string.Join(", ", Enum.GetNames<ResultDeliveryMode>())}.");

                RuleFor(x => x.Request.ResultDelivery!.Url)
                    .NotEmpty().WithMessage("Result delivery URL is required.");

                RuleFor(x => x.Request.ResultDelivery!.Method)
                    .NotEmpty().WithMessage("Result delivery method is required.");
            });

            // Опциональный polling-конфиг
            When(x => x.Request.PollingConfig is not null, () =>
            {
                RuleFor(x => x.Request.PollingConfig!.Field)
                    .NotEmpty().WithMessage("Polling field is required.");
                RuleFor(x => x.Request.PollingConfig!.IntervalSeconds)
                    .GreaterThan(0).WithMessage("Polling interval must be greater than 0.");
            });

            // Опциональная политика повторных попыток
            When(x => x.Request.Retry?.IntervalsSeconds is { Length: > 0 }, () =>
            {
                RuleForEach(x => x.Request.Retry!.IntervalsSeconds)
                    .GreaterThan(0)
                    .WithMessage("Each retry interval must be greater than 0.");
            });
        });
    }
}
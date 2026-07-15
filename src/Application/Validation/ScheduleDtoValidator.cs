using Application.Dto;
using Cronos;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор расписания, гарантирующий взаимоисключаемость полей и корректность данных.
/// </summary>
public sealed class ScheduleDtoValidator : AbstractValidator<ScheduleDto>
{
    public ScheduleDtoValidator()
    {
        // Правило взаимоисключаемости: ровно одно из трёх полей должно быть задано
        RuleFor(x => x)
            .Must(x => CountFilled(x.ExecuteAt, x.Offset, x.Cron) == 1)
            .WithMessage("Exactly one of ExecuteAt, Offset, or Cron must be specified.");

        // Если задан ExecuteAt — должен быть валидным ISO 8601
        When(x => !string.IsNullOrWhiteSpace(x.ExecuteAt), () =>
        {
            RuleFor(x => x.ExecuteAt)
                .Must(BeValidDateTimeOffset)
                .WithMessage("ExecuteAt must be a valid ISO 8601 date-time string.");
        });

        // Если задан Offset — должен соответствовать формату <число><единица>
        When(x => !string.IsNullOrWhiteSpace(x.Offset), () =>
        {
            RuleFor(x => x.Offset)
                .Must(BeValidOffset)
                .WithMessage("Offset must be in format '<number><unit>' where unit is s, m, h, d, or w, and number > 0.");
        });

        // Если задан Cron — обязательна Timezone и корректное cron-выражение
        When(x => !string.IsNullOrWhiteSpace(x.Cron), () =>
        {
            RuleFor(x => x.Timezone)
                .NotEmpty()
                .WithMessage("Timezone is required when Cron is specified.");

            RuleFor(x => x.Cron)
                .Must(BeValidCronExpression)
                .WithMessage("Cron expression is not valid.");

            RuleFor(x => x.Timezone)
                .Must(BeValidTimezone)
                .WithMessage("Timezone is not a valid IANA timezone.");
        });
    }

    private static int CountFilled(params string?[] values) =>
        values.Count(v => !string.IsNullOrWhiteSpace(v));

    private static bool BeValidDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTimeOffset.TryParse(value, out _);
    }

    private static bool BeValidOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2) return false;
        var unit = value[^1];
        if (!"smhdw".Contains(unit)) return false;
        if (!int.TryParse(value[..^1], out var number) || number <= 0) return false;
        return true;
    }

    private static bool BeValidCronExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        try
        {
            // CronFormat.IncludeSeconds поддерживает секунды (6 полей)
            _ = CronExpression.Parse(expression, CronFormat.IncludeSeconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool BeValidTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)) return false;
        return TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _);
    }
}
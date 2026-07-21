using Application.Dto;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор запроса на создание цепочки заданий.
/// </summary>
public sealed class CreateJobChainRequestValidator : AbstractValidator<CreateJobChainRequest>
{
    public CreateJobChainRequestValidator(ChainStepDtoValidator stepValidator)
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(256)
            .WithMessage("Name is required and must be at most 256 characters.");

        RuleFor(x => x.Steps)
            .NotEmpty()
            .WithMessage("At least one step is required.")
            .ForEach(step => step.SetValidator(stepValidator));

        // Проверка последовательности индексов
        RuleFor(x => x.Steps)
            .Must(HaveSequentialIndices)
            .WithMessage("Step indices must be sequential starting from 0.");
    }

    private static bool HaveSequentialIndices(List<ChainStepDto> steps)
    {
        if (steps == null) return false;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].StepIndex != i)
                return false;
        }
        return true;
    }
}
using Application.Commands;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор команды создания цепочки заданий.
/// </summary>
public sealed class CreateJobChainCommandValidator : AbstractValidator<CreateJobChainCommand>
{
    public CreateJobChainCommandValidator(CreateJobChainRequestValidator requestValidator)
    {
        RuleFor(x => x.Request)
            .NotNull()
            .WithMessage("Request body is required.")
            .SetValidator(requestValidator);
    }
}
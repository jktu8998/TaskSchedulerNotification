using Application.Commands;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор команды создания задания.
/// Проверяет вложенный DTO запроса целиком.
/// </summary>
public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator(CreateTaskRequestValidator requestValidator)
    {
        RuleFor(x => x.Request)
            .NotNull().WithMessage("Request body is required.")
            .SetValidator(requestValidator);
    }
}
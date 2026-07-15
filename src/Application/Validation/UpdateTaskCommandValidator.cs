using Application.Commands;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Валидатор команды обновления задания.
/// Проверяет идентификатор старого задания и новые данные.
/// </summary>
public sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator(CreateTaskRequestValidator requestValidator)
    {
        RuleFor(x => x.TaskId)
            .NotEmpty().WithMessage("TaskId is required.");

        RuleFor(x => x.UpdatedFields)
            .NotNull().WithMessage("Updated fields are required.")
            .SetValidator(requestValidator);
    }
}
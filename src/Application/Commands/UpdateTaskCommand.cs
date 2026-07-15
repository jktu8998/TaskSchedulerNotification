
using Application.Dto;
using Application.Interfaces;

namespace Application.Commands;

/// <summary>
/// Команда на изменение задания (отмена текущего и создание нового).
/// Возвращает идентификатор нового задания.
/// </summary>
public sealed record UpdateTaskCommand(Guid TaskId, CreateTaskRequest UpdatedFields) : ICommand<Guid>, ITransactionalCommand;
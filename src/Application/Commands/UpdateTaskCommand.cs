using System;
using Application.Dto;

namespace Application.Commands;

/// <summary>
/// Команда на изменение задания (отмена текущего и создание нового).
/// Возвращает идентификатор нового задания.
/// </summary>
public sealed record UpdateTaskCommand(Guid TaskId, CreateTaskRequest UpdatedFields) : ICommand<Guid>;
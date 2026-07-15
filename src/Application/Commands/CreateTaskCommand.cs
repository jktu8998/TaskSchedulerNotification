
using Application.Dto;
using Application.Interfaces;

namespace Application.Commands;

/// <summary>
/// Команда на создание нового задания.
/// Принимает сырой запрос от API и возвращает идентификатор созданного задания.
/// </summary>
public sealed record CreateTaskCommand(CreateTaskRequest Request) : ICommand<Guid>, ITransactionalCommand;
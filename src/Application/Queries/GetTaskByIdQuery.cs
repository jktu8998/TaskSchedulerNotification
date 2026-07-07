using System;
using Application.Dto;

namespace Application.Queries;

/// <summary>
/// Запрос на получение задания по идентификатору.
/// Возвращает полную информацию о задании в формате TaskResponse.
/// </summary>
public sealed record GetTaskByIdQuery(Guid TaskId) : IQuery<TaskResponse>;
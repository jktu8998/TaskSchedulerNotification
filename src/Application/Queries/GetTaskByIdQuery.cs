using System;
using Application.Dto;

namespace Application.Queries;

/// <summary>
/// Запрос на получение задания по идентификатору.
/// </summary>
public sealed record GetTaskByIdQuery(Guid TaskId) : IQuery<TaskResponse>;
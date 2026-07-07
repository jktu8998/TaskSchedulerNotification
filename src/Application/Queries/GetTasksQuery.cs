using System.Collections.Generic;
using Application.Dto;

namespace Application.Queries;

/// <summary>
/// Запрос на получение списка заданий с фильтрацией и пагинацией.
/// Фильтры опциональны: статус и тип задания.
/// Результаты автоматически ограничиваются текущим отправителем.
/// </summary>
public sealed record GetTasksQuery(
    string SenderId,
    int Skip,
    int Take,
    string? Status = null,
    string? Type = null
) : IQuery<IReadOnlyList<TaskResponse>>;
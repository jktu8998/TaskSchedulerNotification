
using Application.Dto;

namespace Application.Queries;

/// <summary>
/// Запрос на получение логов задания.
/// </summary>
public sealed record GetTaskLogsQuery(Guid TaskId) : IQuery<IReadOnlyList<TaskLogDto>>;
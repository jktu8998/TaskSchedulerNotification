using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Application.Queries;
using Domain.Enums;
using Domain.Interfaces;

namespace Application.Handlers.Query;

/// <summary>
/// Обработчик запроса на получение списка заданий.
/// Извлекает задания текущего отправителя с опциональными фильтрами и пагинацией,
/// маппит их в DTO и возвращает список.
/// </summary>
public sealed class GetTasksQueryHandler : IQueryHandler<GetTasksQuery, IReadOnlyList<TaskResponse>>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IRequestContext _requestContext;

    public GetTasksQueryHandler(ITaskRepository taskRepo, IRequestContext requestContext)
    {
        _taskRepo = taskRepo;
        _requestContext = requestContext;
    }

    public async Task<IReadOnlyList<TaskResponse>> HandleAsync(GetTasksQuery query, CancellationToken cancellationToken = default)
    {
        StatusTask? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<StatusTask>(query.Status, ignoreCase: true, out var parsedStatus))
            statusFilter = parsedStatus;

        TaskType? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Type) && Enum.TryParse<TaskType>(query.Type, ignoreCase: true, out var parsedType))
            typeFilter = parsedType;

        var tasks = await _taskRepo.GetBySenderIdAsync(
            _requestContext.SenderId,
            query.Skip,
            query.Take,
            statusFilter,
            typeFilter,
            cancellationToken);

        return tasks.Select(TaskMapper.MapToResponse).ToList().AsReadOnly();
    }
}
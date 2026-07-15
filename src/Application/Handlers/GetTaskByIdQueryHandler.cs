using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Application.Queries;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик запроса на получение задания по идентификатору.
/// Загружает задание из репозитория и маппит его в TaskResponse.
/// Проверяет принадлежность задания текущему отправителю.
/// </summary>
public sealed class GetTaskByIdQueryHandler : IQueryHandler<GetTaskByIdQuery, TaskResponse>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IRequestContext _requestContext;

    public GetTaskByIdQueryHandler(ITaskRepository taskRepo, IRequestContext requestContext)
    {
        _taskRepo = taskRepo;
        _requestContext = requestContext;
    }

    public async Task<TaskResponse> HandleAsync(GetTaskByIdQuery query, CancellationToken cancellationToken = default)
    {
        var taskId = TaskId.From(query.TaskId);
        var task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);

        if (task == null || task.SenderId != _requestContext.SenderId)
            return null; // или выбросить исключение, но для запросов лучше null

        return TaskMapper.MapToResponse(task);
    }
}
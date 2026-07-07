using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик запроса логов задания.
/// Проверяет принадлежность задания отправителю через репозиторий,
/// затем загружает логи и маппит их в DTO.
/// </summary>
public sealed class GetTaskLogsQueryHandler : IQueryHandler<GetTaskLogsQuery, IReadOnlyList<TaskLogDto>>
{
    private readonly ITaskRepository _taskRepo;
    private readonly ITaskLogRepository _logRepo;
    private readonly IRequestContext _requestContext;

    public GetTaskLogsQueryHandler(ITaskRepository taskRepo, ITaskLogRepository logRepo, IRequestContext requestContext)
    {
        _taskRepo = taskRepo;
        _logRepo = logRepo;
        _requestContext = requestContext;
    }

    public async Task<IReadOnlyList<TaskLogDto>> HandleAsync(GetTaskLogsQuery query, CancellationToken cancellationToken = default)
    {
        var taskId = TaskId.From(query.TaskId);
        var task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);

        // Проверка доступа: только владелец может смотреть логи
        if (task == null || task.SenderId != _requestContext.SenderId)
            return new List<TaskLogDto>().AsReadOnly();

        var logs = await _logRepo.GetByTaskIdAsync(taskId, cancellationToken);
        return logs.Select(log => new TaskLogDto
        {
            Id = log.Id,
            TaskId = log.TaskId.Value,
            Timestamp = new DateTimeOffset(log.Timestamp, TimeSpan.Zero),
            EventType = log.EventType,
            Message = log.Message,
            Details = log.Details
        }).ToList().AsReadOnly();
    }
}
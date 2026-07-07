using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Domain.Entities;
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

        return MapToResponse(task);
    }

    private static TaskResponse MapToResponse(ScheduledTask task)
    {
        return new TaskResponse
        {
            Id = task.Id.Value,
            SenderId = task.SenderId,
            Type = task.Type.ToString(),
            Status = task.Status.ToString(),
            Schedule = MapSchedule(task.Schedule),
            Execution = new ExecutionConfigDto
            {
                Method = task.Execution.Method,
                Url = task.Execution.Url,
                Headers = task.Execution.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Body = task.Execution.Body
            },
            ResultDelivery = task.ResultDelivery != null ? new ResultDeliveryConfigDto
            {
                Mode = task.ResultDelivery.Mode.ToString(),
                Url = task.ResultDelivery.Url,
                Method = task.ResultDelivery.Method,
                Params = task.ResultDelivery.Params
            } : null,
            PollingConfig = task.PollingConfig != null ? new PollingConfigDto
            {
                Field = task.PollingConfig.Field,
                Condition = task.PollingConfig.Condition,
                Value = task.PollingConfig.Value,
                IntervalSeconds = task.PollingConfig.IntervalSeconds,
                VerboseLogging = task.PollingConfig.VerboseLogging
            } : null,
            Retry = new RetryPolicyDto
            {
                IntervalsSeconds = task.RetryPolicy.IntervalsSeconds.ToArray()
            },
            CurrentAttempt = task.CurrentAttempt,
            CreatedAt = new DateTimeOffset(task.CreatedAt, TimeSpan.Zero),
            UpdatedAt = task.UpdatedAt.HasValue ? new DateTimeOffset(task.UpdatedAt.Value, TimeSpan.Zero) : null,
            NextExecutionAt = task.NextExecutionAt.HasValue ? new DateTimeOffset(task.NextExecutionAt.Value, TimeSpan.Zero) : null
        };
    }

    private static ScheduleDto MapSchedule(Schedule schedule)
    {
        return new ScheduleDto
        {
            ExecuteAt = schedule.ExecuteAt?.ToString("o"),
            Offset = schedule.Offset.HasValue ? FormatOffset(schedule.Offset.Value) : null,
            Cron = schedule.CronExpression,
            Timezone = schedule.Timezone
        };
    }

    private static string FormatOffset(TimeSpan offset)
    {
        if (offset.TotalSeconds % 60 == 0 && offset.TotalMinutes % 60 == 0) return $"{(int)offset.TotalHours}h";
        if (offset.TotalSeconds % 60 == 0) return $"{(int)offset.TotalMinutes}m";
        return $"{(int)offset.TotalSeconds}s";
    }
}
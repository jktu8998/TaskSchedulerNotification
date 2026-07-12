using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;

namespace Application.Handlers;

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

        return tasks.Select(MapToResponse).ToList().AsReadOnly();
    }

    private static TaskResponse MapToResponse(ScheduledTask task)
    {
        // Аналогично GetTaskByIdQueryHandler.MapToResponse
        return new TaskResponse
        {
            Id = task.Id.Value,
            SenderId = task.SenderId,
            Type = task.Type.ToString(),
            Status = task.Status.ToString(),
            Schedule = new ScheduleDto
            {
                ExecuteAt = task.Schedule.ExecuteAt?.ToString("o"),
                Offset = task.Schedule.Offset.HasValue ? FormatOffset(task.Schedule.Offset.Value) : null,
                Cron = task.Schedule.CronExpression,
                Timezone = task.Schedule.Timezone
            },
            Execution = new ExecutionConfigDto
            {
                Method = task.Execution.Method,
                Url = task.Execution.Url,
                Headers = task.Execution.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Body = task.Execution.Body,
                TimeoutSeconds = task.Execution.TimeoutSeconds
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
            Retry = new RetryPolicyDto { IntervalsSeconds = task.RetryPolicy.IntervalsSeconds.ToArray() },
            CurrentAttempt = task.CurrentAttempt,
            CreatedAt = new DateTimeOffset(task.CreatedAt, TimeSpan.Zero),
            UpdatedAt = task.UpdatedAt.HasValue ? new DateTimeOffset(task.UpdatedAt.Value, TimeSpan.Zero) : null,
            NextExecutionAt = task.NextExecutionAt.HasValue ? new DateTimeOffset(task.NextExecutionAt.Value, TimeSpan.Zero) : null
        };
    }

    private static string FormatOffset(TimeSpan offset)
    {
        if (offset.TotalMinutes >= 1 && offset.TotalMinutes % 60 == 0) return $"{(int)offset.TotalHours}h";
        if (offset.TotalSeconds >= 60 && offset.TotalSeconds % 60 == 0) return $"{(int)offset.TotalMinutes}m";
        return $"{(int)offset.TotalSeconds}s";
    }
}
using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.Services;

/// <summary>
/// Реализация фабрики заданий для шагов цепочки.
/// </summary>
public sealed class ChainTaskFactory : IChainTaskFactory
{
    private readonly ITaskFactory _taskFactory;

    public ChainTaskFactory(ITaskFactory taskFactory)
    {
        _taskFactory = taskFactory;
    }

    public ScheduledTask CreateTaskForStep(
        ChainStep step,
        TaskId chainId,
        string senderId,
        DateTime utcNow,
        int stepIndex)
    {
        // Формируем DTO запроса
        var request = new CreateTaskRequest
        {
            IdempotencyKey = $"chain:{chainId.Value}:step:{stepIndex}",
            Type = TaskType.OneTime.ToString(),
            Schedule = step.Schedule != null
                ? new ScheduleDto
                {
                    ExecuteAt = step.Schedule.ExecuteAt?.ToString("o"),
                    Offset = step.Schedule.Offset.HasValue ? TaskMapper.FormatOffset(step.Schedule.Offset.Value) : null,
                    Cron = step.Schedule.CronExpression,
                    Timezone = step.Schedule.Timezone
                }
                : new ScheduleDto { Offset = "0s" }, // немедленное выполнение
            Execution = TaskMapper.MapExecutionToDto(step.Execution),
            ResultDelivery = step.ResultDelivery != null ? MapResultDeliveryToDto(step.ResultDelivery) : null,
            PollingConfig = step.PollingConfig != null ? MapPollingConfigToDto(step.PollingConfig) : null,
            Retry = step.RetryPolicy != null
                ? new RetryPolicyDto { IntervalsSeconds = step.RetryPolicy.IntervalsSeconds.ToArray() }
                : null,
            SensitiveData = null // чувствительные данные могут быть внутри Execution, при необходимости добавить
        };

        // Используем универсальную фабрику, передавая chainId и stepIndex
        return _taskFactory.CreateFromRequest(
            request,
            senderId,
            utcNow,
            request.IdempotencyKey,
            chainId,
            stepIndex);
    }

    private static ResultDeliveryConfigDto MapResultDeliveryToDto(ResultDeliveryConfig config) =>
        new()
        {
            Mode = config.Mode.ToString(),
            Url = config.Url,
            Method = config.Method,
            Params = config.Params
        };

    private static PollingConfigDto MapPollingConfigToDto(PollingConfig config) =>
        new()
        {
            Field = config.Field,
            Condition = config.Condition,
            Value = config.Value,
            IntervalSeconds = config.IntervalSeconds,
            VerboseLogging = config.VerboseLogging
        };
}
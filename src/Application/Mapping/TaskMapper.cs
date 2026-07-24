using Application.Dto;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.Mapping;

/// <summary>
/// Статический маппер для преобразования доменных объектов в DTO ответов API.
/// Не содержит зависимостей от инфраструктуры (шифрование, БД).
/// Для создания агрегатов будет использоваться внедряемый ITaskFactory .
/// </summary>
public static class TaskMapper
{
    /// <summary>
    /// Преобразует агрегат ScheduledTask в DTO ответа API.
    /// </summary>
    public static TaskResponse MapToResponse(ScheduledTask task)
    {
        return new TaskResponse
        {
            Id = task.Id.Value,
            SenderId = task.SenderId.ToString(), 
            Type = task.Type.ToString(),
            Status = task.Status.ToString(),
            Schedule = MapSchedule(task.Schedule),
            Execution = MapExecutionConfig(task.Execution),
            ResultDelivery = MapResultDelivery(task.ResultDelivery),
            PollingConfig = MapPollingConfig(task.PollingConfig),
            Retry = MapRetryPolicy(task.RetryPolicy),
            CurrentAttempt = task.CurrentAttempt,
            CreatedAt = new DateTimeOffset(task.CreatedAt, TimeSpan.Zero),
            UpdatedAt = task.UpdatedAt.HasValue
                ? new DateTimeOffset(task.UpdatedAt.Value, TimeSpan.Zero)
                : null,
            NextExecutionAt = task.NextExecutionAt.HasValue
                ? new DateTimeOffset(task.NextExecutionAt.Value, TimeSpan.Zero)
                : null,
            RawPayload = task.RawPayload,
            Metadata = task.Metadata.Data.Count > 0
                ? task.Metadata.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                : null 
        };
    }
    
    public static TaskSnapshotDto ToSnapshot(ScheduledTask task)
    {
        return new TaskSnapshotDto
        {
            IdempotencyKey = task.IdempotencyKey,
            SenderId = task.SenderId.ToString(),
            Type = task.Type.ToString(),
            Schedule = MapSchedule(task.Schedule),
            Execution = MapExecutionToDto(task.Execution),
            ResultDelivery = task.ResultDelivery is not null
                ? new ResultDeliveryConfigDto
                {
                    Mode = task.ResultDelivery.Mode.ToString(),
                    Url = task.ResultDelivery.Url,
                    Method = task.ResultDelivery.Method,
                    Params = task.ResultDelivery.Params
                }
                : null,
            PollingConfig = task.PollingConfig is not null
                ? new PollingConfigDto
                {
                    Field = task.PollingConfig.Field,
                    Condition = task.PollingConfig.Condition,
                    Value = task.PollingConfig.Value,
                    IntervalSeconds = task.PollingConfig.IntervalSeconds,
                    VerboseLogging = task.PollingConfig.VerboseLogging
                }
                : null,
            Retry = new RetryPolicyDto
            {
                IntervalsSeconds = task.RetryPolicy.IntervalsSeconds.ToArray()
            },
            EncryptedSensitiveData = task.EncryptedSensitiveData,
            RawPayload = task.RawPayload,
            Metadata = task.Metadata.Data.Count > 0
                ? task.Metadata.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                : null
        };
    }

    /// <summary>
    /// Фабричный метод создания стратегии выполнения по DTO.
    /// Используется как в статическом маппере, так и в будущем ITaskFactory.
    /// </summary>
    public static ExecutionStrategy CreateExecutionStrategy(ExecutionConfigDto dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        var type = (dto.ExecutionType ?? "http").ToLowerInvariant();

        return type switch
        {
            "http" => new HttpExecutionConfig(
                dto.Method,
                dto.Url,
                dto.Headers,
                dto.Body,
                dto.TimeoutSeconds),
            // TODO будущие типы: "grpc" => new GrpcExecutionConfig(...)
            _ => throw new ArgumentException($"Unsupported execution type: {type}")
        };
    }

    // Приватные хелперы маппинга составных частей ответа
    public static ExecutionConfigDto MapExecutionToDto(ExecutionStrategy strategy) => MapExecutionConfig(strategy);
    
    private static ScheduleDto MapSchedule(Schedule schedule) => new()
    {
        ExecuteAt = schedule.ExecuteAt?.ToString("o"),
        Offset = schedule.Offset.HasValue ? FormatOffset(schedule.Offset.Value) : null,
        Cron = schedule.CronExpression,
        Timezone = schedule.Timezone
    };

    private static ExecutionConfigDto MapExecutionConfig(ExecutionStrategy strategy)
    {
        if (strategy is null)
        {
            // Логируем, что задание повреждено, но не роняем весь список
            return new ExecutionConfigDto
            {
                ExecutionType = "unknown"
            };
        }
        if (strategy is HttpExecutionConfig http)
        {
            return new ExecutionConfigDto
            {
                ExecutionType = "http",
                Method = http.Method,
                Url = http.Url,
                Headers = http.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Body = http.Body,
                TimeoutSeconds = http.TimeoutSeconds
            };
        }

        // Для неизвестных типов возвращаем минимальную информацию
        return new ExecutionConfigDto
        {
            ExecutionType = strategy.StrategyType
        };
    }

    private static ResultDeliveryConfigDto? MapResultDelivery(ResultDeliveryConfig? config) =>
        config is null ? null : new ResultDeliveryConfigDto
        {
            Mode = config.Mode.ToString(),
            Url = config.Url,
            Method = config.Method,
            Params = config.Params
        };

    private static PollingConfigDto? MapPollingConfig(PollingConfig? config) =>
        config is null ? null : new PollingConfigDto
        {
            Field = config.Field,
            Condition = config.Condition,
            Value = config.Value,
            IntervalSeconds = config.IntervalSeconds,
            VerboseLogging = config.VerboseLogging
        };

    private static RetryPolicyDto MapRetryPolicy(RetryPolicy policy) => new()
    {
        IntervalsSeconds = policy.IntervalsSeconds.ToArray()
    };

    //потом можно добавить недели и месяцы 
    public static string FormatOffset(TimeSpan offset)
    {
        if (offset.TotalSeconds == 0) return "0s";
        if (offset.TotalDays >= 1 && offset.TotalDays % 1 == 0) return $"{(int)offset.TotalDays}d";
        if (offset.TotalHours >= 1 && offset.TotalHours % 1 == 0) return $"{(int)offset.TotalHours}h";
        if (offset.TotalMinutes >= 1 && offset.TotalMinutes % 1 == 0) return $"{(int)offset.TotalMinutes}m";
        return $"{(int)offset.TotalSeconds}s";
    }
}
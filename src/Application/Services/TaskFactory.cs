using System.Text.Json;
using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.Services;

/// <summary>
/// Фабрика создания агрегата ScheduledTask из DTO.
/// Инкапсулирует маппинг, шифрование, метаданные и планирование.
/// </summary>
public sealed class TaskFactory : ITaskFactory
{
    private readonly IEncryptionService _encryption;

    public TaskFactory(IEncryptionService encryption)
    {
        _encryption = encryption;
    }

    public ScheduledTask CreateFromRequest(CreateTaskRequest request, string senderId, DateTime utcNow)
    {
        // 1. Расписание
        var schedule = ScheduleMapper.MapSchedule(request.Schedule);

        // 2. Стратегия выполнения
        var strategy = TaskMapper.CreateExecutionStrategy(request.Execution);

        // 3. ResultDelivery
        ResultDeliveryConfig? resultDelivery = null;
        if (request.ResultDelivery is not null)
        {
            var mode = Enum.Parse<ResultDeliveryMode>(request.ResultDelivery.Mode, ignoreCase: true);
            resultDelivery = new ResultDeliveryConfig(
                mode,
                request.ResultDelivery.Url,
                request.ResultDelivery.Method,
                request.ResultDelivery.Params);
        }

        // 4. PollingConfig
        PollingConfig? pollingConfig = null;
        if (request.PollingConfig is not null)
        {
            pollingConfig = new PollingConfig(
                request.PollingConfig.Field,
                request.PollingConfig.Condition,
                request.PollingConfig.Value,
                request.PollingConfig.IntervalSeconds,
                request.PollingConfig.VerboseLogging);
        }

        // 5. RetryPolicy
        RetryPolicy retryPolicy = request.Retry?.IntervalsSeconds is { Length: > 0 } intervals
            ? new RetryPolicy(intervals)
            : RetryPolicy.Default;

        // 6. TaskMetadata из расширенных полей
        TaskMetadata metadata = TaskMetadata.Empty;
        if (request.ExtensionData is { Count: > 0 })
        {
            var stringData = new Dictionary<string, string>();
            foreach (var kvp in request.ExtensionData)
            {
                string value = kvp.Value switch
                {
                    JsonElement jsonElement => jsonElement.ValueKind == JsonValueKind.String
                        ? jsonElement.GetString()!
                        : jsonElement.GetRawText(),
                    string s => s,
                    _ => kvp.Value?.ToString() ?? string.Empty
                };
                stringData[kvp.Key] = value;
            }
            metadata = new TaskMetadata(stringData);
        }

        // 7. Шифрование sensitive data
        string? encrypted = null;
        if (!string.IsNullOrWhiteSpace(request.SensitiveData))
            encrypted = _encryption.Encrypt(request.SensitiveData);

        // 8. Создание агрегата
        var task = new ScheduledTask(
            TaskId.New(),
            senderId,
            Enum.Parse<TaskType>(request.Type, ignoreCase: true),
            schedule,
            strategy,
            resultDelivery,
            pollingConfig,
            retryPolicy,
            encrypted,
            utcNow,
            metadata);

        // 9. Вычисление и планирование первого запуска
        var nextExecutionAt = task.Schedule.GetNextOccurrence(task.CreatedAt)
            ?? throw new InvalidOperationException("Cron expression has no future occurrences.");
        task.ScheduleTask(utcNow, nextExecutionAt);

        return task;
    }
}
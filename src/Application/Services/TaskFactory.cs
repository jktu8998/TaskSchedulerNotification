using System.Text.Json;
using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.Services;

public sealed class TaskFactory : ITaskFactory
{
    private readonly IEncryptionService _encryption;

    public TaskFactory(IEncryptionService encryption) => _encryption = encryption;

    public ScheduledTask CreateFromRequest(CreateTaskRequest request, string senderId, DateTime utcNow)
    {
        string? encrypted = null;
        if (!string.IsNullOrWhiteSpace(request.SensitiveData))
            encrypted = _encryption.Encrypt(request.SensitiveData);

        return BuildTask(
            request.Type, request.Schedule, request.Execution,
            request.ResultDelivery, request.PollingConfig, request.Retry,
            encrypted, request.ExtensionData, senderId, utcNow);
    }

    public ScheduledTask CreateFromSnapshot(TaskSnapshotDto snapshot, string senderId, DateTime utcNow)
    {
        // Конвертируем метаданные в ExtensionData для переиспользования BuildTask
        Dictionary<string, object>? extensionData = null;
        if (snapshot.Metadata is { Count: > 0 })
        {
            extensionData = new Dictionary<string, object>(snapshot.Metadata.Count);
            foreach (var kvp in snapshot.Metadata)
                extensionData[kvp.Key] = kvp.Value;
        }

        return BuildTask(
            snapshot.Type, snapshot.Schedule, snapshot.Execution,
            snapshot.ResultDelivery, snapshot.PollingConfig, snapshot.Retry,
            snapshot.EncryptedSensitiveData, // уже зашифровано
            extensionData, senderId, utcNow);
    }

    private ScheduledTask BuildTask(
        string type, ScheduleDto scheduleDto, ExecutionConfigDto execDto,
        ResultDeliveryConfigDto? resultDeliveryDto, PollingConfigDto? pollingDto,
        RetryPolicyDto? retryDto, string? encryptedSensitive,
        Dictionary<string, object>? extensionData, string senderId, DateTime utcNow)
    {
        var schedule = ScheduleMapper.MapSchedule(scheduleDto);
        var strategy = TaskMapper.CreateExecutionStrategy(execDto);

        ResultDeliveryConfig? resultDelivery = null;
        if (resultDeliveryDto is not null)
        {
            var mode = Enum.Parse<ResultDeliveryMode>(resultDeliveryDto.Mode, ignoreCase: true);
            resultDelivery = new ResultDeliveryConfig(mode, resultDeliveryDto.Url,
                resultDeliveryDto.Method, resultDeliveryDto.Params);
        }

        PollingConfig? pollingConfig = null;
        if (pollingDto is not null)
        {
            pollingConfig = new PollingConfig(pollingDto.Field, pollingDto.Condition,
                pollingDto.Value, pollingDto.IntervalSeconds, pollingDto.VerboseLogging);
        }

        RetryPolicy retryPolicy = retryDto?.IntervalsSeconds is { Length: > 0 } intervals
            ? new RetryPolicy(intervals)
            : RetryPolicy.Default;

        TaskMetadata metadata = TaskMetadata.Empty;
        if (extensionData is { Count: > 0 })
        {
            var stringData = new Dictionary<string, string>();
            foreach (var kvp in extensionData)
            {
                string value = kvp.Value switch
                {
                    JsonElement jsonEl => jsonEl.ValueKind == JsonValueKind.String
                        ? jsonEl.GetString()!
                        : jsonEl.GetRawText(),
                    string s => s,
                    _ => kvp.Value?.ToString() ?? string.Empty
                };
                stringData[kvp.Key] = value;
            }
            metadata = new TaskMetadata(stringData);
        }

        var task = new ScheduledTask(
            TaskId.New(), senderId, Enum.Parse<TaskType>(type, ignoreCase: true),
            schedule, strategy, resultDelivery, pollingConfig, retryPolicy,
            encryptedSensitive, utcNow, metadata);

        var nextExecution = task.Schedule.GetNextOccurrence(task.CreatedAt)
            ?? throw new InvalidOperationException("Cron expression has no future occurrences.");
        task.ScheduleTask(utcNow, nextExecution);

        return task;
    }
}
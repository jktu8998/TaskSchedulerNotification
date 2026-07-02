using System;
using  Domain.ValueObjects;

namespace  Domain.Entities;

public sealed class PollingState
{
    public TaskId TaskId { get; private set; }
    public string? LastResponseJson { get; private set; }
    public DateTime? LastCheckedAt { get; private set; }

    public PollingState(TaskId taskId)
    {
        TaskId = taskId;
    }

    public void UpdateState(string? responseJson)
    {
        LastResponseJson = responseJson;
        LastCheckedAt = DateTime.UtcNow;
    }
}
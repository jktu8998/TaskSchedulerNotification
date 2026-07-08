using System;
using Domain.Entities;
using Domain.ValueObjects;
using Xunit;

namespace Domain.Tests.Entities;

public class PollingStateTests
{
    [Fact]
    public void Constructor_SetsTaskId()
    {
        // Arrange
        var taskId = TaskId.New();

        // Act
        var state = new PollingState(taskId);

        // Assert
        Assert.Equal(taskId, state.TaskId);
    }

    [Fact]
    public void UpdateState_SetsLastResponseJsonAndLastCheckedAt()
    {
        // Arrange
        var taskId = TaskId.New();
        var state = new PollingState(taskId);
        var utcNow = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        const string responseJson = "{\"status\":\"ok\"}";

        // Act
        state.UpdateState(responseJson, utcNow);

        // Assert
        Assert.Equal(responseJson, state.LastResponseJson);
        Assert.Equal(utcNow, state.LastCheckedAt);
    }
}
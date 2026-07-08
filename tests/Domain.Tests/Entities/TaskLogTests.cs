using System;
using Domain.Entities;
using Domain.ValueObjects;
using Xunit;

namespace Domain.Tests.Entities;

public class TaskLogTests
{
    [Fact]
    public void Constructor_AssignsAllProperties()
    {
        // Arrange
        var taskId = TaskId.New();
        var utcNow = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        const string eventType = "STATUS_CHANGE";
        const string? message = "Task scheduled";
        const string? details = "Next execution at...";

        // Act
        var log = new TaskLog(taskId, eventType, utcNow, message, details);

        // Assert
        Assert.Equal(taskId, log.TaskId);
        Assert.Equal(eventType, log.EventType);
        Assert.Equal(utcNow, log.Timestamp);
        Assert.Equal(message, log.Message);
        Assert.Equal(details, log.Details);
    }

    [Fact]
    public void Constructor_WithNullMessageAndDetails_Works()
    {
        // Arrange
        var taskId = TaskId.New();
        var utcNow = DateTime.UtcNow;

        // Act
        var log = new TaskLog(taskId, "ERROR", utcNow);

        // Assert
        Assert.Null(log.Message);
        Assert.Null(log.Details);
    }
}
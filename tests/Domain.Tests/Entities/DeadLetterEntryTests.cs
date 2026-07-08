using System;
using Domain.Entities;
using Domain.ValueObjects;
using Xunit;

namespace Domain.Tests.Entities;

public class DeadLetterEntryTests
{
    [Fact]
    public void Constructor_AssignsAllProperties()
    {
        // Arrange
        var taskId = TaskId.New();
        const string senderId = "service-A";
        const string snapshot = "{\"original\":true}";
        const string? errorDetails = "Timeout";
        var movedAt = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var entry = new DeadLetterEntry(taskId, senderId, snapshot, errorDetails, movedAt);

        // Assert
        Assert.Equal(taskId, entry.TaskId);
        Assert.Equal(senderId, entry.SenderId);
        Assert.Equal(snapshot, entry.OriginalTaskSnapshot);
        Assert.Equal(errorDetails, entry.ErrorDetails);
        Assert.Equal(movedAt, entry.MovedAt);
    }

    [Fact]
    public void Constructor_WithNullErrorDetails_IsAllowed()
    {
        // Arrange
        var taskId = TaskId.New();
        var movedAt = DateTime.UtcNow;

        // Act
        var entry = new DeadLetterEntry(taskId, "sender", "snap", null, movedAt);

        // Assert
        Assert.Null(entry.ErrorDetails);
    }
}
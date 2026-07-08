using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Xunit;

namespace Domain.Tests.DomainEvents;

public class TaskCreatedEventTests
{
    [Fact]
    public void ExplicitInterfaceImplementation_ReturnsCorrectTaskId()
    {
        // Arrange
        var taskId = TaskId.New();
        var utcNow = DateTime.UtcNow;
        var task = new ScheduledTask(
            taskId,
            "sender",
            TaskType.OneTime,
            Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
            new ExecutionConfig("GET", "https://test.com"),
            null, null, null, null,
            utcNow);

        // Act
        IDomainEvent domainEvent = new TaskCreatedEvent(task);

        // Assert
        Assert.Equal(taskId, domainEvent.TaskId);
    }
}
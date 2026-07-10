using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.DomainEventHandlers;
using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;

namespace Application.Tests.DomainEventHandlers;

public class UniversalEventLoggerTests
{
    private readonly Mock<ITaskLogRepository> _logRepoMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public UniversalEventLoggerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
    }

    [Fact]
    public async Task HandleAsync_CreatesTaskLogWithCorrectEventType()
    {
        // Arrange
        var taskId = TaskId.New();
        var createdEvent = new TaskCreatedEvent(new ScheduledTask(
            taskId, "sender", TaskType.OneTime,
            Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
            new ExecutionConfig("GET", "https://example.com"),
            null, null, null, null, _utcNow));

        var logger = new UniversalEventLogger<TaskCreatedEvent>(_logRepoMock.Object, _dateTimeMock.Object);
        TaskLog? capturedLog = null;
        _logRepoMock
            .Setup(l => l.AddAsync(It.IsAny<TaskLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskLog, CancellationToken>((log, _) => capturedLog = log);

        // Act
        await logger.HandleAsync(createdEvent, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedLog);
        Assert.Equal(taskId, capturedLog.TaskId);
        Assert.Equal("TaskCreated", capturedLog.EventType); // "TaskCreatedEvent" без "Event"
        Assert.Equal(_utcNow, capturedLog.Timestamp);
        Assert.NotNull(capturedLog.Details);
        Assert.Contains(taskId.Value.ToString(), capturedLog.Details); // сериализованный JSON содержит TaskId
        _logRepoMock.Verify(l => l.AddAsync(It.IsAny<TaskLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SerializesEventToDetailsJson()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var scheduledEvent = new TaskScheduledEvent(taskId);
        var logger = new UniversalEventLogger<TaskScheduledEvent>(_logRepoMock.Object, _dateTimeMock.Object);
        TaskLog? capturedLog = null;
        _logRepoMock
            .Setup(l => l.AddAsync(It.IsAny<TaskLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskLog, CancellationToken>((log, _) => capturedLog = log);

        // Act
        await logger.HandleAsync(scheduledEvent, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedLog);
        var expectedJson = JsonSerializer.Serialize(scheduledEvent);
        Assert.Equal(expectedJson, capturedLog.Details);
    }

    [Fact]
    public async Task HandleAsync_PassesCancellationTokenToRepository()
    {
        // Arrange
        var taskId = TaskId.New();
        var completedEvent = new TaskCompletedEvent(taskId);
        var logger = new UniversalEventLogger<TaskCompletedEvent>(_logRepoMock.Object, _dateTimeMock.Object);
        var ct = new CancellationTokenSource().Token;

        // Act
        await logger.HandleAsync(completedEvent, ct);

        // Assert
        _logRepoMock.Verify(l => l.AddAsync(It.IsAny<TaskLog>(), ct), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ForDifferentEventTypes_UsesCorrectNames()
    {
        // Проверим для нескольких типов событий
        var logger1 = new UniversalEventLogger<TaskFailedEvent>(_logRepoMock.Object, _dateTimeMock.Object);
        var logger2 = new UniversalEventLogger<TaskMovedToDlqEvent>(_logRepoMock.Object, _dateTimeMock.Object);

        TaskLog? log1 = null, log2 = null;
        _logRepoMock.Setup(l => l.AddAsync(It.IsAny<TaskLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskLog, CancellationToken>((log, _) =>
            {
                if (log1 == null) log1 = log;
                else log2 = log;
            });

        await logger1.HandleAsync(new TaskFailedEvent(TaskId.New()), CancellationToken.None);
        await logger2.HandleAsync(new TaskMovedToDlqEvent(TaskId.New()), CancellationToken.None);

        Assert.NotNull(log1);
        Assert.NotNull(log2);
        Assert.Equal("TaskFailed", log1.EventType);
        Assert.Equal("TaskMovedToDlq", log2.EventType);
    }
}
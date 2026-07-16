using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Dto;
using Application.Handlers;
using Application.Handlers.Query;
using Application.Interfaces;
using Application.Queries;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;

namespace Application.Tests.Handlers;

public class GetTaskLogsQueryHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<ITaskLogRepository> _logRepoMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly GetTaskLogsQueryHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public GetTaskLogsQueryHandlerTests()
    {
        _requestContextMock.Setup(r => r.SenderId).Returns("test-sender");
        _handler = new GetTaskLogsQueryHandler(_taskRepoMock.Object, _logRepoMock.Object, _requestContextMock.Object);
    }

    private static TaskLog CreateLog(TaskId taskId, string eventType = "Created", DateTime? timestamp = null, string? message = null, string? details = null)
    {
        var log = new TaskLog(taskId, eventType, timestamp ?? DateTime.UtcNow, message, details);
        // Устанавливаем Id через рефлексию, т.к. у него нет публичного сеттера и в тестах это не критично
        typeof(TaskLog).GetProperty("Id")?.SetValue(log, 1L);
        return log;
    }

    [Fact]
    public async Task HandleAsync_ReturnsLogs()
    {
        // Arrange
        var taskId = TaskId.New();
        var task = new ScheduledTask(
            taskId, "test-sender", TaskType.OneTime,
            Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
            new ExecutionConfig("GET", "https://example.com"),
            null, null, null, null, _utcNow);
        _taskRepoMock.Setup(r => r.GetByIdAsync(taskId, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        var logs = new List<TaskLog>
        {
            CreateLog(taskId, "Created", _utcNow, null, "{\"key\":\"value\"}"),
            CreateLog(taskId, "Executing", _utcNow.AddSeconds(1), "Started", null)
        };
        _logRepoMock.Setup(r => r.GetByTaskIdAsync(taskId, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(logs);

        var query = new GetTaskLogsQuery(taskId.Value);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Collection(result,
            first =>
            {
                Assert.Equal(taskId.Value, first.TaskId);
                Assert.Equal("Created", first.EventType);
                Assert.Equal(new DateTimeOffset(_utcNow, TimeSpan.Zero), first.Timestamp);
                Assert.Equal("{\"key\":\"value\"}", first.Details);
                Assert.Null(first.Message);
            },
            second =>
            {
                Assert.Equal(taskId.Value, second.TaskId);
                Assert.Equal("Executing", second.EventType);
                Assert.Equal("Started", second.Message);
                Assert.Null(second.Details);
            });
    }

    [Fact]
    public async Task HandleAsync_TaskNotFound_ReturnsEmptyList()
    {
        var taskId = TaskId.New();
        _taskRepoMock.Setup(r => r.GetByIdAsync(taskId, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync((ScheduledTask?)null);
        var query = new GetTaskLogsQuery(taskId.Value);

        var result = await _handler.HandleAsync(query, _ct);

        Assert.NotNull(result);
        Assert.Empty(result);
        _logRepoMock.Verify(r => r.GetByTaskIdAsync(It.IsAny<TaskId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WrongSender_ReturnsEmptyList()
    {
        var taskId = TaskId.New();
        var task = new ScheduledTask(
            taskId, "other-sender", TaskType.OneTime,
            Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
            new ExecutionConfig("GET", "https://example.com"),
            null, null, null, null, _utcNow);
        _taskRepoMock.Setup(r => r.GetByIdAsync(taskId, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
        var query = new GetTaskLogsQuery(taskId.Value);

        var result = await _handler.HandleAsync(query, _ct);

        Assert.NotNull(result);
        Assert.Empty(result);
        _logRepoMock.Verify(r => r.GetByTaskIdAsync(It.IsAny<TaskId>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
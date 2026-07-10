using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Dto;
using Application.Handlers;
using Application.Interfaces;
using Application.Queries;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;

namespace Application.Tests.Handlers;

public class GetTaskByIdQueryHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly GetTaskByIdQueryHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public GetTaskByIdQueryHandlerTests()
    {
        _requestContextMock.Setup(r => r.SenderId).Returns("test-sender");
        _handler = new GetTaskByIdQueryHandler(_taskRepoMock.Object, _requestContextMock.Object);
    }

    private ScheduledTask CreateFullTask(Guid taskId, string senderId = "test-sender")
    {
        return new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            TaskType.Periodic,
            Schedule.FromCron("0 5 * * * *", "Europe/Moscow"),
            new ExecutionConfig("POST", "https://api.example.com", new Dictionary<string, string> { { "X-Key", "val" } }, "body", 120),
            new ResultDeliveryConfig(ResultDeliveryMode.FixedCall, "https://cb.example.com", "PUT", "{\"result\":true}"),
            new PollingConfig("status", "changed", "value", 300),
            new RetryPolicy(new[] { 10, 20, 30 }),
            "encrypted-secret",
            _utcNow);
    }

    [Fact]
    public async Task HandleAsync_TaskExists_ReturnsMappedTaskResponse()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateFullTask(taskId);
        task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        var query = new GetTaskByIdQuery(taskId);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskId, result.Id);
        Assert.Equal("test-sender", result.SenderId);
        Assert.Equal("Periodic", result.Type);
        Assert.Equal("Scheduled", result.Status);
        Assert.NotNull(result.Schedule);
        Assert.Equal("0 5 * * * *", result.Schedule.Cron);
        Assert.Equal("Europe/Moscow", result.Schedule.Timezone);
        Assert.NotNull(result.Execution);
        Assert.Equal("POST", result.Execution.Method);
        Assert.Equal("https://api.example.com", result.Execution.Url);
        Assert.Equal("body", result.Execution.Body);
        Assert.Equal(120, result.Execution.TimeoutSeconds);
        Assert.NotNull(result.Execution.Headers);
        Assert.Equal("val", result.Execution.Headers["X-Key"]);
        Assert.NotNull(result.ResultDelivery);
        Assert.Equal("FixedCall", result.ResultDelivery.Mode);
        Assert.Equal("https://cb.example.com", result.ResultDelivery.Url);
        Assert.Equal("PUT", result.ResultDelivery.Method);
        Assert.Equal("{\"result\":true}", result.ResultDelivery.Params);
        Assert.NotNull(result.PollingConfig);
        Assert.Equal("status", result.PollingConfig.Field);
        Assert.Equal("changed", result.PollingConfig.Condition);
        Assert.Equal("value", result.PollingConfig.Value);
        Assert.Equal(300, result.PollingConfig.IntervalSeconds);
        Assert.NotNull(result.Retry);
        Assert.Equal(new[] { 10, 20, 30 }, result.Retry.IntervalsSeconds);
        Assert.Equal(0, result.CurrentAttempt);
        Assert.Equal(new DateTimeOffset(_utcNow, TimeSpan.Zero), result.CreatedAt);
        Assert.NotNull(result.UpdatedAt);
        Assert.NotNull(result.NextExecutionAt);
    }

    [Fact]
    public async Task HandleAsync_TaskNotFound_ReturnsNull()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync((ScheduledTask?)null);
        var query = new GetTaskByIdQuery(taskId);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_WrongSender_ReturnsNull()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateFullTask(taskId, "other-sender");
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        var query = new GetTaskByIdQuery(taskId);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Null(result);
    }
}
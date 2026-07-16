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

public class GetTasksQueryHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly GetTasksQueryHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public GetTasksQueryHandlerTests()
    {
        _requestContextMock.Setup(r => r.SenderId).Returns("test-sender");
        _handler = new GetTasksQueryHandler(_taskRepoMock.Object, _requestContextMock.Object);
    }

    private ScheduledTask CreateTask(Guid taskId, string senderId = "test-sender", TaskType type = TaskType.OneTime, StatusTask status = StatusTask.Scheduled, int? timeoutSeconds = null)
    {
        var task = new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            type,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://api.example.com", timeoutSeconds: timeoutSeconds),
            null, null, RetryPolicy.Default, null,
            _utcNow);
        if (status == StatusTask.Scheduled)
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
        else if (status == StatusTask.Completed)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
            task.Enqueue(_utcNow);
            task.StartExecution(_utcNow, TimeSpan.FromSeconds(30));
            task.CompleteSuccessfully(_utcNow);
        }
        return task;
    }

    [Fact]
    public async Task HandleAsync_NoFilters_ReturnsAllTasksForSender()
    {
        // Arrange
        var tasks = new List<ScheduledTask>
        {
            CreateTask(Guid.NewGuid(), "test-sender"),
            CreateTask(Guid.NewGuid(), "test-sender"),
        };
        _taskRepoMock.Setup(r => r.GetBySenderIdAsync(
                "test-sender", 0, 10, null, null, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(tasks);

        var query = new GetTasksQuery(0, 10);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, r =>
        {
            Assert.Equal("test-sender", r.SenderId);
            Assert.Equal("Scheduled", r.Status);
            Assert.NotNull(r.Execution);
            Assert.Null(r.Execution.TimeoutSeconds);
        });
    }

    [Fact]
    public async Task HandleAsync_WithStatusFilter_ParsesAndFilters()
    {
        // Arrange
        var completedTask = CreateTask(Guid.NewGuid(), "test-sender", TaskType.OneTime, StatusTask.Completed);
        var scheduledTask = CreateTask(Guid.NewGuid(), "test-sender", TaskType.OneTime, StatusTask.Scheduled);
        var allTasks = new List<ScheduledTask> { completedTask, scheduledTask };

        _taskRepoMock.Setup(r => r.GetBySenderIdAsync(
                "test-sender", 0, 10, Domain.Enums.StatusTask.Completed, null, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { completedTask });

        var query = new GetTasksQuery(0, 10, Status: "Completed");

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Single(result);
        Assert.Equal("Completed", result[0].Status);
    }

    [Fact]
    public async Task HandleAsync_WithTypeFilter_ParsesAndFilters()
    {
        // Arrange
        var periodicTask = CreateTask(Guid.NewGuid(), "test-sender", TaskType.Periodic, StatusTask.Scheduled);
        var oneTimeTask = CreateTask(Guid.NewGuid(), "test-sender", TaskType.OneTime, StatusTask.Scheduled);
        var allTasks = new List<ScheduledTask> { periodicTask, oneTimeTask };

        _taskRepoMock.Setup(r => r.GetBySenderIdAsync(
                "test-sender", 0, 10, null, Domain.Enums.TaskType.Periodic, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { periodicTask });

        var query = new GetTasksQuery(0, 10, Type: "Periodic");

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Single(result);
        Assert.Equal("Periodic", result[0].Type);
    }

    [Fact]
    public async Task HandleAsync_InvalidStatusAndType_Ignored()
    {
        // Arrange
        var tasks = new List<ScheduledTask> { CreateTask(Guid.NewGuid()) };
        _taskRepoMock.Setup(r => r.GetBySenderIdAsync(
                "test-sender", 0, 10, null, null, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(tasks);

        var query = new GetTasksQuery(0, 10, Status: "Invalid", Type: "Invalid");

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task HandleAsync_EmptyList_ReturnsEmpty()
    {
        // Arrange
        _taskRepoMock.Setup(r => r.GetBySenderIdAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<Domain.Enums.StatusTask?>(), It.IsAny<Domain.Enums.TaskType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduledTask>());

        var query = new GetTasksQuery(0, 10);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_MapsAllFieldsCorrectly()
    {
        // Arrange: задание со всеми полными опциональными конфигурациями
        var taskId = Guid.NewGuid();
        var task = new ScheduledTask(
            TaskId.From(taskId),
            "test-sender",
            TaskType.Periodic,
            Schedule.FromCron("0 5 * * * *", "UTC"),
            new ExecutionConfig("POST", "https://example.com", new Dictionary<string, string> { { "Auth", "Bearer" } }, "req-body", 45),
            new ResultDeliveryConfig(ResultDeliveryMode.ForwardResponse, "https://callback.example.com", "POST"),
            new PollingConfig("field", "greater_than", "10", 90),
            new RetryPolicy(new[] { 15, 30 }),
            "enc",
            _utcNow);
        task.ScheduleTask(_utcNow, _utcNow.AddMinutes(5));

        _taskRepoMock.Setup(r => r.GetBySenderIdAsync(
                "test-sender", 0, 5, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { task });

        var query = new GetTasksQuery(0, 5);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        var dto = Assert.Single(result);
        Assert.Equal(taskId, dto.Id);
        Assert.Equal("Scheduled", dto.Status);
        Assert.Equal("Periodic", dto.Type);
        Assert.Equal("0 5 * * * *", dto.Schedule.Cron);
        Assert.Equal("UTC", dto.Schedule.Timezone);
        Assert.Equal("POST", dto.Execution.Method);
        Assert.Equal("https://example.com", dto.Execution.Url);
        Assert.Equal("req-body", dto.Execution.Body);
        Assert.Equal(45, dto.Execution.TimeoutSeconds);
        Assert.Equal("Bearer", dto.Execution.Headers?["Auth"]);
        Assert.NotNull(dto.ResultDelivery);
        Assert.Equal("ForwardResponse", dto.ResultDelivery.Mode);
        Assert.Equal("https://callback.example.com", dto.ResultDelivery.Url);
        Assert.NotNull(dto.PollingConfig);
        Assert.Equal("field", dto.PollingConfig.Field);
        Assert.Equal(90, dto.PollingConfig.IntervalSeconds);
        Assert.Equal(new[] { 15, 30 }, dto.Retry.IntervalsSeconds);
        Assert.Equal(0, dto.CurrentAttempt);
        Assert.NotNull(dto.CreatedAt);
        Assert.NotNull(dto.UpdatedAt);
        Assert.NotNull(dto.NextExecutionAt);
    }
}
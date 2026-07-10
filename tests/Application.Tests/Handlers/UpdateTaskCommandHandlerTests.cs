using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Dto;
using Application.Handlers;
using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;
using TaskStatus = Domain.Enums.TaskStatus;

namespace Application.Tests.Handlers;

public class UpdateTaskCommandHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IEncryptionService> _encryptionMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly UpdateTaskCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public UpdateTaskCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _requestContextMock.Setup(r => r.SenderId).Returns("test-sender");

        _handler = new UpdateTaskCommandHandler(
            _taskRepoMock.Object,
            _encryptionMock.Object,
            _dateTimeMock.Object,
            _requestContextMock.Object,
            _unitOfWorkMock.Object,
            _dispatcherMock.Object
        );
    }

    private ScheduledTask CreateExistingTask(Guid taskId, string senderId = "test-sender")
    {
        return new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://old-url.com"),
            null, null, null, null,
            _utcNow);
    }

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_ValidUpdate_ReturnsNewTaskId()
    {
        // Arrange
        var oldTaskId = Guid.NewGuid();
        var existingTask = CreateExistingTask(oldTaskId);
        _taskRepoMock
            .Setup(r => r.GetByIdAsync(TaskId.From(oldTaskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(existingTask);

        var updatedRequest = new CreateTaskRequest
        {
            Type = "Periodic",
            Schedule = new ScheduleDto { Cron = "0 0 0 * * *", Timezone = "UTC" },
            Execution = new ExecutionConfigDto
            {
                Method = "POST",
                Url = "https://new-url.com",
                TimeoutSeconds = 45
            },
            Retry = new RetryPolicyDto { IntervalsSeconds = new[] { 15, 30 } }
        };
        var command = new UpdateTaskCommand(oldTaskId, updatedRequest);

        ScheduledTask? capturedOldTask = null;
        ScheduledTask? capturedNewTask = null;

        _taskRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedOldTask = t)
            .Returns(Task.CompletedTask);
        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedNewTask = t)
            .Returns(Task.CompletedTask);

        // Act
        var newTaskId = await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotEqual(Guid.Empty, newTaskId);
        Assert.NotEqual(oldTaskId, newTaskId);

        // Старое задание отменено
        Assert.NotNull(capturedOldTask);
        Assert.Equal(oldTaskId, capturedOldTask.Id.Value);
        Assert.Equal(TaskStatus.Cancelled, capturedOldTask.Status);

        // Новое задание создано с обновлёнными полями
        Assert.NotNull(capturedNewTask);
        Assert.Equal(TaskType.Periodic, capturedNewTask.Type);
        Assert.Equal("0 0 0 * * *", capturedNewTask.Schedule.CronExpression);
        Assert.Equal("POST", capturedNewTask.Execution.Method);
        Assert.Equal("https://new-url.com", capturedNewTask.Execution.Url);
        Assert.Equal(45, capturedNewTask.Execution.TimeoutSeconds);
        Assert.Equal(2, capturedNewTask.RetryPolicy.MaxAttempts);
        Assert.Equal(new[] { 15, 30 }, capturedNewTask.RetryPolicy.IntervalsSeconds);
        Assert.Equal("test-sender", capturedNewTask.SenderId); // из контекста

        // Транзакция
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // События для обоих заданий
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsNotNull<IReadOnlyCollection<IDomainEvent>>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WithSensitiveData_Encrypts()
    {
        // Arrange
        var oldTaskId = Guid.NewGuid();
        var existingTask = CreateExistingTask(oldTaskId);
        _taskRepoMock
            .Setup(r => r.GetByIdAsync(TaskId.From(oldTaskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTask);

        var updatedRequest = new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { Offset = "3h" },
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://example.com" },
            SensitiveData = "new-secret"
        };
        var command = new UpdateTaskCommand(oldTaskId, updatedRequest);
        _encryptionMock.Setup(e => e.Encrypt("new-secret")).Returns("encrypted-new-secret");

        ScheduledTask? capturedNewTask = null;
        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedNewTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedNewTask);
        Assert.Equal("encrypted-new-secret", capturedNewTask.EncryptedSensitiveData);
        _encryptionMock.Verify(e => e.Encrypt("new-secret"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PreservesSenderIdFromContext()
    {
        // Arrange: другой отправитель в контексте
        _requestContextMock.Setup(r => r.SenderId).Returns("original-sender");
        var oldTaskId = Guid.NewGuid();
        var existingTask = CreateExistingTask(oldTaskId, "original-sender"); // совпадает с контекстом
        _taskRepoMock
            .Setup(r => r.GetByIdAsync(TaskId.From(oldTaskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTask);

        var updatedRequest = new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { ExecuteAt = "2026-08-01T00:00:00Z" },
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://example.com" }
        };
        var command = new UpdateTaskCommand(oldTaskId, updatedRequest);
        ScheduledTask? capturedNewTask = null;
        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedNewTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedNewTask);
        Assert.Equal("original-sender", capturedNewTask.SenderId); // из контекста
    }
}
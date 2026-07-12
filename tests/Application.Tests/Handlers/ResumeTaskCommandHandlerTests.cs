using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Handlers;
using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;

namespace Application.Tests.Handlers;

public class ResumeTaskCommandHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly ResumeTaskCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public ResumeTaskCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _requestContextMock.Setup(r => r.SenderId).Returns("test-sender");

        _handler = new ResumeTaskCommandHandler(
            _taskRepoMock.Object,
            _unitOfWorkMock.Object,
            _dateTimeMock.Object,
            _dispatcherMock.Object,
            _requestContextMock.Object
        );
    }

    private ScheduledTask CreateTask(Guid taskId, string senderId = "test-sender", StatusTask status = StatusTask.Paused)
    {
        var task = new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://example.com"),
            null, null, null, null,
            _utcNow);

        if (status == StatusTask.Paused)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
            task.Pause(_utcNow);
        }
        else if (status == StatusTask.Scheduled)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
        }
        return task;
    }

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_ResumePausedTask_Success()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "test-sender", StatusTask.Paused);
        _taskRepoMock
            .Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        ScheduledTask? capturedTask = null;
        _taskRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        var command = new ResumeTaskCommand(taskId);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(taskId, capturedTask.Id.Value);
        Assert.Equal(StatusTask.Scheduled, capturedTask.Status);

        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsNotNull<IReadOnlyCollection<IDomainEvent>>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    // ========== Негативные тесты ==========

    [Fact]
    public async Task HandleAsync_TaskNotFound_ThrowsInvalidOperationException()
    {
        var taskId = Guid.NewGuid();
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync((ScheduledTask?)null);

        var command = new ResumeTaskCommand(taskId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("Task not found or access denied.", ex.Message);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WrongSender_ThrowsInvalidOperationException()
    {
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "other-sender", StatusTask.Paused);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        var command = new ResumeTaskCommand(taskId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("Task not found or access denied.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_TaskNotInPaused_ThrowsInvalidOperationException()
    {
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "test-sender", StatusTask.Scheduled);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        var command = new ResumeTaskCommand(taskId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CommitFails_RollsBackAndThrows()
    {
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "test-sender", StatusTask.Paused);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
        _unitOfWorkMock.Setup(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .ThrowsAsync(new Exception("DB error"));

        var command = new ResumeTaskCommand(taskId);
        var ex = await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("DB error", ex.Message);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }
}
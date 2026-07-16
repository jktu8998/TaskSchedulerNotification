using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Handlers;
using Application.Handlers.CrudHandlers;
using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;

namespace Application.Tests.Handlers;

public class CancelTaskCommandHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly CancelTaskCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public CancelTaskCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _requestContextMock.Setup(r => r.SenderId).Returns("test-sender");

        _handler = new CancelTaskCommandHandler(
            _taskRepoMock.Object,
            _unitOfWorkMock.Object,
            _dateTimeMock.Object,
            _dispatcherMock.Object,
            _requestContextMock.Object
        );
    }

    private ScheduledTask CreateTask(Guid taskId, string senderId = "test-sender", StatusTask status = StatusTask.Scheduled)
    {
        var task = new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://old-url.com"),
            null, null, null, null,
            _utcNow);

        if (status == StatusTask.Scheduled)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
        }
        // Для других статусов позже добавим в негативные тесты
        return task;
    }

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_CancelScheduledTask_Success()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var scheduledTask = CreateTask(taskId, "test-sender", StatusTask.Scheduled);
        _taskRepoMock
            .Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(scheduledTask);

        ScheduledTask? capturedTask = null;
        _taskRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        var command = new CancelTaskCommand(taskId);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(taskId, capturedTask.Id.Value);
        Assert.Equal(StatusTask.Cancelled, capturedTask.Status);
        Assert.Null(capturedTask.LockedUntil); // не должно быть блокировки

        // Транзакция: начали, коммит
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        // События диспатчатся
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsNotNull<IReadOnlyCollection<IDomainEvent>>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // Проверка, что ClearDomainEvents был вызван (косвенно, событий нет после завершения)
    }

    [Fact]
    public async Task HandleAsync_CancelFromExecuting_ClearsLock()
    {
        // Arrange: задание в статусе Executing (с блокировкой)
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "test-sender", StatusTask.Scheduled);
        // Переводим в Executing
        task.Enqueue(_utcNow);
        task.StartExecution(_utcNow, TimeSpan.FromSeconds(30));
        Assert.Equal(StatusTask.Executing, task.Status);
        Assert.NotNull(task.LockedUntil);

        _taskRepoMock
            .Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        ScheduledTask? capturedTask = null;
        _taskRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        var command = new CancelTaskCommand(taskId);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(StatusTask.Cancelled, capturedTask.Status);
        Assert.Null(capturedTask.LockedUntil); // блокировка сброшена
    }
    
    // ========== Негативные тесты ==========
    // В том же файле CancelTaskCommandHandlerTests.cs, внутри класса

    [Fact]
    public async Task HandleAsync_TaskNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync((ScheduledTask?)null);
    
        var command = new CancelTaskCommand(taskId);
    
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("Task not found or access denied.", ex.Message);
    
        // Никаких изменений в БД
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_WrongSender_ThrowsInvalidOperationException()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "other-sender", StatusTask.Scheduled); // другой отправитель
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
    
        var command = new CancelTaskCommand(taskId);
    
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("Task not found or access denied.", ex.Message);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_TaskInCompletedStatus_ThrowsInvalidOperationException()
    {
        // Arrange: задание в Completed
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "test-sender", StatusTask.Scheduled);
        task.Enqueue(_utcNow);
        task.StartExecution(_utcNow, TimeSpan.FromSeconds(30));
        task.CompleteSuccessfully(_utcNow);
        Assert.Equal(StatusTask.Completed, task.Status);

        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        var command = new CancelTaskCommand(taskId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));

        // Транзакция была начата, но откатилась из-за исключения
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_CommitFails_RollsBackAndThrows()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTask(taskId, "test-sender", StatusTask.Scheduled);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
    
        _unitOfWorkMock.Setup(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .ThrowsAsync(new Exception("DB error"));
    
        var command = new CancelTaskCommand(taskId);
    
        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("DB error", ex.Message);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // UpdateAsync вызывался до коммита
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }
}
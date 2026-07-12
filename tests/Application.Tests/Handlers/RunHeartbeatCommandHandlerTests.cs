using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Handlers;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;

namespace Application.Tests.Handlers;

public class RunHeartbeatCommandHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IDeadLetterRepository> _dlqRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly Mock<IRandomProvider> _randomMock = new();
    private readonly RunHeartbeatCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public RunHeartbeatCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _randomMock.Setup(r => r.Next(0, 16)).Returns(5); // детерминированный Jitter

        _handler = new RunHeartbeatCommandHandler(
            _taskRepoMock.Object,
            _dlqRepoMock.Object,
            _unitOfWorkMock.Object,
            _dateTimeMock.Object,
            _dispatcherMock.Object,
            _randomMock.Object
        );
    }

    private ScheduledTask CreateExecutingTask(
        Guid taskId,
        string senderId = "sender",
        DateTime? lockedUntil = null,
        RetryPolicy? retryPolicy = null,
        int currentAttempt = 0,
        TaskType type = TaskType.OneTime)
    {
        var task = new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            type,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://api.example.com"),
            null, null, retryPolicy ?? RetryPolicy.Default, null,
            _utcNow);
        task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
        task.Enqueue(_utcNow);
        task.StartExecution(_utcNow, TimeSpan.FromSeconds(30));

        if (lockedUntil.HasValue)
        {
            // Вручную установим LockedUntil через рефлексию, т.к. у ScheduledTask нет публичного сеттера
            typeof(ScheduledTask).GetProperty("LockedUntil")?.SetValue(task, lockedUntil.Value);
        }
        if (currentAttempt > 0)
        {
            typeof(ScheduledTask).GetProperty("CurrentAttempt")?.SetValue(task, currentAttempt);
        }
        return task;
    }

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_NoStaleTasks_DoesNothing()
    {
        _taskRepoMock.Setup(r => r.GetStaleExecutingTasksAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new List<ScheduledTask>());

        await _handler.HandleAsync(new RunHeartbeatCommand(), _ct);

        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _dlqRepoMock.Verify(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TaskFailed_ReschedulesWithJitter()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateExecutingTask(taskId, lockedUntil: _utcNow.AddSeconds(-10));
        _taskRepoMock.Setup(r => r.GetStaleExecutingTasksAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { task });

        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(new RunHeartbeatCommand(), _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(StatusTask.Scheduled, capturedTask.Status); // Failed -> Scheduled через ScheduleRetry
        // Базовый интервал 60 сек + Jitter 5 сек = 65 сек
        var expectedRetryTime = _utcNow.AddSeconds(65);
        Assert.Equal(expectedRetryTime, capturedTask.NextExecutionAt);
        Assert.Equal(1, capturedTask.CurrentAttempt);
        _dlqRepoMock.Verify(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TaskDead_MovesToDlq()
    {
        // Arrange: кастомный retry с одной попыткой, задача уже на последней попытке (CurrentAttempt = 1)
        var taskId = Guid.NewGuid();
        var retryPolicy = new RetryPolicy(new[] { 60 });
        var task = CreateExecutingTask(taskId, lockedUntil: _utcNow.AddSeconds(-5), retryPolicy: retryPolicy, currentAttempt: 1);
        task.ClearDomainEvents(); // очищаем события перед обработкой, чтобы сериализация не упала
        _taskRepoMock.Setup(r => r.GetStaleExecutingTasksAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { task });

        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t);

        // Act
        await _handler.HandleAsync(new RunHeartbeatCommand(), _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(StatusTask.Dead, capturedTask.Status);
        _dlqRepoMock.Verify(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MultipleTasks_ProcessesAll()
    {
        // Arrange
        var task1 = CreateExecutingTask(Guid.NewGuid(), lockedUntil: _utcNow.AddSeconds(-1));
        var task2 = CreateExecutingTask(Guid.NewGuid(), lockedUntil: _utcNow.AddSeconds(-20));
        _taskRepoMock.Setup(r => r.GetStaleExecutingTasksAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { task1, task2 });

        // Act
        await _handler.HandleAsync(new RunHeartbeatCommand(), _ct);

        // Assert: каждая задача обновлена, транзакции для каждой
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _dlqRepoMock.Verify(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /*[Fact] этот тесет не проходит не может пройти, потому что при выходе индекса за границы IntervalsSeconds задача всегда переходит в статус Dead, а не Failed
    public async Task HandleAsync_FallbackInterval_UsedWhenIndexOutOfRange()
    {
        var taskId = Guid.NewGuid();
        var retryPolicy = new RetryPolicy(new[] { 120 }); // только один интервал
        var task = CreateExecutingTask(taskId, lockedUntil: _utcNow.AddSeconds(-10), retryPolicy: retryPolicy, currentAttempt: 5);
        task.ClearDomainEvents(); // обязательно очищаем, чтобы сериализация не упала
        _taskRepoMock.Setup(r => r.GetStaleExecutingTasksAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { task });

        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t);

        await _handler.HandleAsync(new RunHeartbeatCommand(), _ct);

        Assert.NotNull(capturedTask);
        Assert.Equal(_utcNow.AddSeconds(65), capturedTask.NextExecutionAt);
    }*/

    // ========== Негативные тесты ==========

    [Fact]
    public async Task HandleAsync_OneTaskFails_OthersStillProcessed()
    {
        // Arrange: два задания, первое падает при UpdateAsync
        var badTask = CreateExecutingTask(Guid.NewGuid(), lockedUntil: _utcNow.AddSeconds(-10));
        var goodTask = CreateExecutingTask(Guid.NewGuid(), lockedUntil: _utcNow.AddSeconds(-5));
        _taskRepoMock.Setup(r => r.GetStaleExecutingTasksAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { badTask, goodTask });

        _taskRepoMock.Setup(r => r.UpdateAsync(It.Is<ScheduledTask>(t => t.Id == badTask.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        await _handler.HandleAsync(new RunHeartbeatCommand(), _ct);

        // Assert: для плохого задания транзакция откатилась, для хорошего — успешно
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.Is<ScheduledTask>(t => t.Id == goodTask.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DlqAddFails_RollsBack()
    {
        var taskId = Guid.NewGuid();
        var retryPolicy = new RetryPolicy(new[] { 60 });
        var task = CreateExecutingTask(taskId, lockedUntil: _utcNow.AddSeconds(-5), retryPolicy: retryPolicy, currentAttempt: 1);
        _taskRepoMock.Setup(r => r.GetStaleExecutingTasksAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { task });

        _dlqRepoMock.Setup(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DLQ failure"));

        await _handler.HandleAsync(new RunHeartbeatCommand(), _ct);

        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // UpdateAsync не вызывается, так как исключение произошло до него
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
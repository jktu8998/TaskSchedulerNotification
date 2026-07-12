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
using Newtonsoft.Json;
using Xunit;

namespace Application.Tests.Handlers;

public class RetryFromDlqCommandHandlerTests
{
    private readonly Mock<IDeadLetterRepository> _dlqRepoMock = new();
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly RetryFromDlqCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public RetryFromDlqCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _requestContextMock.Setup(r => r.SenderId).Returns("sender-original");

        _handler = new RetryFromDlqCommandHandler(
            _dlqRepoMock.Object,
            _taskRepoMock.Object,
            _dateTimeMock.Object,
            _unitOfWorkMock.Object,
            _dispatcherMock.Object,
            _requestContextMock.Object
        );
    }

    private static string SerializeTask(ScheduledTask task)
    {
        return JsonConvert.SerializeObject(task);
    }

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_Success_ReturnsNewTaskId()
    {
        // Arrange
        var originalTaskId = Guid.NewGuid();
        var originalTask = new ScheduledTask(
            TaskId.From(originalTaskId),
            "sender-original",
            TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("POST", "https://target.example.com", new Dictionary<string, string> { { "Auth", "Bearer x" } }, "body", 45),
            new ResultDeliveryConfig(ResultDeliveryMode.ForwardResponse, "https://callback.example.com", "POST"),
            new PollingConfig("status", "changed", "value", 120),
            new RetryPolicy(new[] { 30, 60 }),
            "encrypted-data",
            _utcNow);

        // Очищаем события, как это делает обработчик перед сохранением в DLQ
        originalTask.ClearDomainEvents();
        var snapshot = SerializeTask(originalTask);
        var dlqEntry = new DeadLetterEntry(originalTask.Id, "sender-original", snapshot, "Some error", _utcNow);
        typeof(DeadLetterEntry).GetProperty("Id")?.SetValue(dlqEntry, 101L);

        _dlqRepoMock.Setup(r => r.GetByIdAsync(101L, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(dlqEntry);

        ScheduledTask? capturedNewTask = null;
        _taskRepoMock.Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedNewTask = t);

        var command = new RetryFromDlqCommand(101L);

        // Act
        var newTaskId = await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotEqual(Guid.Empty, newTaskId);
        Assert.NotNull(capturedNewTask);
        Assert.NotEqual(originalTaskId, capturedNewTask.Id.Value);
        Assert.Equal("sender-original", capturedNewTask.SenderId);
        Assert.Equal(TaskType.OneTime, capturedNewTask.Type);
        Assert.Equal(StatusTask.Scheduled, capturedNewTask.Status);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), capturedNewTask.Schedule.ExecuteAt?.UtcDateTime);
        Assert.Equal("POST", capturedNewTask.Execution.Method);
        Assert.Equal("https://target.example.com", capturedNewTask.Execution.Url);
        Assert.Equal(45, capturedNewTask.Execution.TimeoutSeconds);
        Assert.Equal("body", capturedNewTask.Execution.Body);
        Assert.NotNull(capturedNewTask.ResultDelivery);
        Assert.Equal(ResultDeliveryMode.ForwardResponse, capturedNewTask.ResultDelivery.Mode);
        Assert.NotNull(capturedNewTask.PollingConfig);
        Assert.Equal("status", capturedNewTask.PollingConfig.Field);
        Assert.Equal(2, capturedNewTask.RetryPolicy.MaxAttempts);
        Assert.Equal(new[] { 30, 60 }, capturedNewTask.RetryPolicy.IntervalsSeconds);
        Assert.Equal("encrypted-data", capturedNewTask.EncryptedSensitiveData);
        Assert.Equal(0, capturedNewTask.CurrentAttempt);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), capturedNewTask.NextExecutionAt);

        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _dlqRepoMock.Verify(r => r.RemoveAsync(101L, It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsNotNull<IReadOnlyCollection<IDomainEvent>>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PreservesPeriodicSchedule()
    {
        // Arrange
        var originalTask = new ScheduledTask(
            TaskId.New(),
            "sender-original",
            TaskType.Periodic,
            Schedule.FromCron("0 15 * * * *", "UTC"),
            new ExecutionConfig("GET", "https://example.com"),
            null, null, null, null,
            _utcNow);
        originalTask.ClearDomainEvents(); // очистка событий
        var snapshot = SerializeTask(originalTask);
        var dlqEntry = new DeadLetterEntry(originalTask.Id, "sender-original", snapshot, null, _utcNow);
        typeof(DeadLetterEntry).GetProperty("Id")?.SetValue(dlqEntry, 200L);

        _dlqRepoMock.Setup(r => r.GetByIdAsync(200L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dlqEntry);

        ScheduledTask? capturedNewTask = null;
        _taskRepoMock.Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedNewTask = t);

        // Act
        await _handler.HandleAsync(new RetryFromDlqCommand(200L), _ct);

        // Assert
        Assert.NotNull(capturedNewTask);
        Assert.Equal(TaskType.Periodic, capturedNewTask.Type);
        Assert.Equal("0 15 * * * *", capturedNewTask.Schedule.CronExpression);
    }
    
    // Негативные тесты для RetryFromDlqCommandHandler
    
    [Fact]
    public async Task HandleAsync_DlqEntryNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        _dlqRepoMock.Setup(r => r.GetByIdAsync(999L, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync((DeadLetterEntry?)null);
        var command = new RetryFromDlqCommand(999L);
    
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("DLQ entry not found.", ex.Message);
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _dlqRepoMock.Verify(r => r.RemoveAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_InvalidSnapshot_ThrowsJsonReaderException()
    {
        // Arrange: создаём запись DLQ с некорректным JSON (десериализация выбросит JsonReaderException)
        var invalidSnapshot = "{ invalid json }";
        var dlqEntry = new DeadLetterEntry(TaskId.New(), "sender-original", invalidSnapshot, "Error", _utcNow);
        typeof(DeadLetterEntry).GetProperty("Id")?.SetValue(dlqEntry, 102L);
        _dlqRepoMock.Setup(r => r.GetByIdAsync(102L, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(dlqEntry);
        var command = new RetryFromDlqCommand(102L);

        // Act & Assert
        await Assert.ThrowsAsync<JsonReaderException>(() => _handler.HandleAsync(command, _ct));
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _dlqRepoMock.Verify(r => r.RemoveAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_WrongSender_ThrowsInvalidOperationException()
    {
        // Arrange: snapshot содержит задание с другим SenderId
        var otherTask = new ScheduledTask(TaskId.New(), "other-sender", TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://example.com"), null, null, null, null, _utcNow);
        otherTask.ClearDomainEvents();
        var snapshot = JsonConvert.SerializeObject(otherTask);
        var dlqEntry = new DeadLetterEntry(otherTask.Id, "other-sender", snapshot, null, _utcNow);
        typeof(DeadLetterEntry).GetProperty("Id")?.SetValue(dlqEntry, 103L);
        _dlqRepoMock.Setup(r => r.GetByIdAsync(103L, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(dlqEntry);
        var command = new RetryFromDlqCommand(103L);
    
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("Task not found or access denied.", ex.Message);
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _dlqRepoMock.Verify(r => r.RemoveAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_CommitFails_RollsBackAndThrows()
    {
        // Arrange: успешная загрузка DLQ, но коммит падает
        var originalTask = new ScheduledTask(
            TaskId.New(), "sender-original", TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://example.com"), null, null, null, null, _utcNow);
        originalTask.ClearDomainEvents();
        var snapshot = JsonConvert.SerializeObject(originalTask);
        var dlqEntry = new DeadLetterEntry(originalTask.Id, "sender-original", snapshot, null, _utcNow);
        typeof(DeadLetterEntry).GetProperty("Id")?.SetValue(dlqEntry, 104L);
        _dlqRepoMock.Setup(r => r.GetByIdAsync(104L, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(dlqEntry);
        _unitOfWorkMock.Setup(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .ThrowsAsync(new Exception("DB error"));
        var command = new RetryFromDlqCommand(104L);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("DB error", ex.Message);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // AddAsync был вызван, RemoveAsync — тоже, но всё откатится
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _dlqRepoMock.Verify(r => r.RemoveAsync(It.IsAny<long>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }
}
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

public class RunSchedulingCommandHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IOutboxRepository> _outboxRepoMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly RunSchedulingCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public RunSchedulingCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _handler = new RunSchedulingCommandHandler(
            _taskRepoMock.Object,
            _outboxRepoMock.Object,
            _dateTimeMock.Object,
            _unitOfWorkMock.Object,
            _dispatcherMock.Object
        );
    }

    private ScheduledTask CreateScheduledTask(DateTime nextExecutionAt, Guid? taskId = null)
    {
        var task = new ScheduledTask(
            TaskId.From(taskId ?? Guid.NewGuid()),
            "sender",
            TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://test.com"),
            null, null, null, null,
            _utcNow);
        task.ScheduleTask(_utcNow, nextExecutionAt);
        return task;
    }

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_MultipleTasks_AllProcessedSuccessfully()
    {
        // Arrange
        var task1 = CreateScheduledTask(_utcNow.AddMinutes(-5), Guid.NewGuid());
        var task2 = CreateScheduledTask(_utcNow.AddMinutes(-1), Guid.NewGuid());
        var readyTasks = new List<ScheduledTask> { task1, task2 };

        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(readyTasks);

        // Act
        await _handler.HandleAsync(new RunSchedulingCommand(), _ct);

        // Assert
        // Каждое задание должно быть обновлено (Enqueue + сохранение)
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));

        // Для каждого задания должна быть создана запись в Outbox
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == task1.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == task2.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);

        // Транзакция для каждого задания: Begin, Commit, Rollback ни разу
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);

        // События диспатчатся для каждого задания
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsAny<IReadOnlyCollection<IDomainEvent>>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_TaskEnqueueFails_IsolatesAndContinues()
    {
        // Arrange
        var goodTask = CreateScheduledTask(_utcNow.AddMinutes(-5), Guid.NewGuid());
        var badTask = CreateScheduledTask(_utcNow.AddMinutes(-3), Guid.NewGuid());
        var readyTasks = new List<ScheduledTask> { badTask, goodTask };

        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(readyTasks);

        // Делаем так, что при вызове Enqueue на первом задании (badTask) выбрасывается исключение
        // Для этого мы не можем замокать сам метод Enqueue, но можем сделать UpdateAsync для badTask падающим.
        // Однако исключение должно возникнуть ДО UpdateAsync при вызове task.Enqueue(). Поскольку Enqueue – это метод агрегата,
        // и он не виртуальный, мы не можем его замокать. Чтобы симулировать сбой, можно передать задание с неверным статусом,
        // чтобы Enqueue бросило InvalidOperationException. Например, задание уже в Queued. Тогда Enqueue упадёт.
        // Но у нас фабрика создаёт Scheduled, мы можем вручную перевести в Queued до передачи в список.
        var alreadyQueuedTask = CreateScheduledTask(_utcNow.AddMinutes(-3), Guid.NewGuid());
        alreadyQueuedTask.Enqueue(_utcNow); // теперь статус Queued, повторный Enqueue вызовет исключение
        var goodTask2 = CreateScheduledTask(_utcNow.AddMinutes(-1), Guid.NewGuid());
        var tasks = new List<ScheduledTask> { alreadyQueuedTask, goodTask2 };

        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(tasks);

        // Act
        await _handler.HandleAsync(new RunSchedulingCommand(), _ct);

        // Assert: для первого задания транзакция откатилась, для второго – закоммитилась
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);

        // Outbox добавлен только для успешного задания
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == goodTask2.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == alreadyQueuedTask.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Never);

        // UpdateAsync вызван только для успешного задания (для плохого не должен, потому что Enqueue упал до вызова UpdateAsync)
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OutboxMessageCreatedCorrectly()
    {
        // Arrange
        var task = CreateScheduledTask(_utcNow.AddMinutes(-10), Guid.NewGuid());
        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new[] { task });

        OutboxMessage? capturedOutbox = null;
        _outboxRepoMock
            .Setup(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<OutboxMessage, CancellationToken>((msg, _) => capturedOutbox = msg);

        // Act
        await _handler.HandleAsync(new RunSchedulingCommand(), _ct);

        // Assert
        Assert.NotNull(capturedOutbox);
        Assert.Equal(task.Id, capturedOutbox.TaskId);
        Assert.Equal(_utcNow, capturedOutbox.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_NoScheduledTasks_DoesNothing()
    {
        // Arrange
        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(new List<ScheduledTask>());

        // Act
        await _handler.HandleAsync(new RunSchedulingCommand(), _ct);

        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxRepoMock.Verify(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsAny<IReadOnlyCollection<IDomainEvent>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    // ========== Негативные тесты ==========
    
    [Fact]
    public async Task HandleAsync_OutboxAddFails_RollsBackAndContinues()
    {
        // Arrange
        var badTask = CreateScheduledTask(_utcNow.AddMinutes(-5), Guid.NewGuid());
        var goodTask = CreateScheduledTask(_utcNow.AddMinutes(-1), Guid.NewGuid());
        var tasks = new List<ScheduledTask> { badTask, goodTask };
    
        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(tasks);
    
        // На первом задании AddAsync в Outbox бросает исключение
        _outboxRepoMock
            .Setup(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == badTask.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Outbox failure"));
    
        // Act
        await _handler.HandleAsync(new RunSchedulingCommand(), _ct);
    
        // Assert
        // Для плохого задания транзакция откатилась
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once); // только хорошее
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    
        // UpdateAsync вызывался для обоих (для плохого до ошибки Outbox)
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
    
        // Outbox добавлен только для хорошего
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == goodTask.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }
    
    [Fact]
    public async Task HandleAsync_UpdateTaskFails_RollsBackAndContinues()
    {
        // Arrange
        var badTask = CreateScheduledTask(_utcNow.AddMinutes(-5), Guid.NewGuid());
        var goodTask = CreateScheduledTask(_utcNow.AddMinutes(-1), Guid.NewGuid());
        var tasks = new List<ScheduledTask> { badTask, goodTask };
    
        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(tasks);
    
        // На первом задании UpdateAsync бросает исключение
        _taskRepoMock
            .Setup(r => r.UpdateAsync(It.Is<ScheduledTask>(t => t.Id == badTask.Id), It.Is<CancellationToken>(ct => ct == _ct)))
            .ThrowsAsync(new Exception("DB update failure"));
    
        // Act
        await _handler.HandleAsync(new RunSchedulingCommand(), _ct);
    
        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    
        // Outbox добавлен только для хорошего
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == goodTask.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == badTask.Id), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_CommitFails_RollsBackAndContinues()
    {
        // Arrange
        var badTask = CreateScheduledTask(_utcNow.AddMinutes(-5), Guid.NewGuid());
        var goodTask = CreateScheduledTask(_utcNow.AddMinutes(-1), Guid.NewGuid());
        var tasks = new List<ScheduledTask> { badTask, goodTask };
    
        _taskRepoMock
            .Setup(r => r.GetScheduledBeforeAsync(_utcNow, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(tasks);
    
        // Коммит для первого задания падает
        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback(() =>
            {
                // Используем счётчик, чтобы упасть только на первом вызове
            });
        // Более точно: через последовательность
        _unitOfWorkMock.SetupSequence(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .ThrowsAsync(new Exception("Commit error"))
            .Returns(Task.CompletedTask);
    
        // Act
        await _handler.HandleAsync(new RunSchedulingCommand(), _ct);
    
        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    
        // Outbox добавлен для обоих, но первый откатился, поэтому удалён (но мы не проверяем удаление, просто убеждаемся, что для хорошего задачи Outbox и Update прошли)
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _outboxRepoMock.Verify(o => o.AddAsync(It.Is<OutboxMessage>(m => m.TaskId == goodTask.Id), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }
}
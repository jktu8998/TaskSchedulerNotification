using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Dto;
using Application.Handlers;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;
using TaskStatus = Domain.Enums.TaskStatus;

namespace Application.Tests.Handlers;

public class UpdateTaskCommandHandlerNegativeTests
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

    public UpdateTaskCommandHandlerNegativeTests()
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

    // Вспомогательный метод для создания существующего задания
    private ScheduledTask CreateTask(Guid taskId, string senderId, TaskStatus status = TaskStatus.Scheduled)
    {
        var task = new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            TaskType.OneTime,
            Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://old-url.com"),
            null, null, null, null,
            _utcNow);

        // Принудительно установим статус, если требуется (через последовательность переходов)
        if (status == TaskStatus.Completed)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
            task.Enqueue(_utcNow);
            task.StartExecution(_utcNow, TimeSpan.FromSeconds(30));
            task.CompleteSuccessfully(_utcNow);
        }
        else if (status == TaskStatus.Cancelled)
        {
            task.Cancel(_utcNow);
        }
        else if (status == TaskStatus.Dead)
        {
            // Создадим с кастомной политикой в 1 попытку, чтобы после MarkFailed перешёл в Dead
            var customRetry = new RetryPolicy(new[] { 60 });
            var deadTask = new ScheduledTask(
                TaskId.From(taskId),
                senderId,
                TaskType.OneTime,
                Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
                new ExecutionConfig("GET", "https://old-url.com"),
                null, null, customRetry, null,
                _utcNow);
            deadTask.ScheduleTask(_utcNow, _utcNow.AddHours(1));
            deadTask.Enqueue(_utcNow);
            deadTask.StartExecution(_utcNow, TimeSpan.FromSeconds(30));
            deadTask.MarkFailed(_utcNow);
            return deadTask; // статус Dead
        }
        return task;
    }

    [Fact]
    public async Task HandleAsync_TaskNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync((ScheduledTask?)null);

        var command = new UpdateTaskCommand(taskId, new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { Offset = "1h" },
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://test.com" }
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("Task not found or access denied.", ex.Message);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WrongSender_ThrowsInvalidOperationException()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var existingTask = CreateTask(taskId, "other-sender"); // не совпадает с контекстом
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(existingTask);

        var command = new UpdateTaskCommand(taskId, new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { Offset = "1h" },
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://test.com" }
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("Task not found or access denied.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_InvalidSchedule_ThrowsArgumentException()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var existingTask = CreateTask(taskId, "test-sender");
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(existingTask);

        var command = new UpdateTaskCommand(taskId, new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { ExecuteAt = null, Offset = null, Cron = null }, // невалидно
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://test.com" }
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _handler.HandleAsync(command, _ct));
        Assert.Contains("ExecuteAt, Offset или Cron", ex.Message);
        // Транзакция не должна начаться
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TaskInFinalStatusCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var completedTask = CreateTask(taskId, "test-sender", TaskStatus.Completed);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(completedTask);

        var command = new UpdateTaskCommand(taskId, new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { Offset = "2h" },
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://test.com" }
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));

        // Транзакция не должна начинаться
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CommitFails_RollsBackAndThrows()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var existingTask = CreateTask(taskId, "test-sender");
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(existingTask);

        var command = new UpdateTaskCommand(taskId, new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { Offset = "1h" },
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://test.com" }
        });

        _unitOfWorkMock.Setup(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .ThrowsAsync(new Exception("DB error"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(command, _ct));
        Assert.Equal("DB error", ex.Message);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_EncryptionFails_Throws()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var existingTask = CreateTask(taskId, "test-sender");
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(existingTask);

        var command = new UpdateTaskCommand(taskId, new CreateTaskRequest
        {
            Type = "OneTime",
            Schedule = new ScheduleDto { Offset = "1h" },
            Execution = new ExecutionConfigDto { Method = "GET", Url = "https://test.com" },
            SensitiveData = "my-secret"
        });

        _encryptionMock.Setup(e => e.Encrypt(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Encryption failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, _ct));
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
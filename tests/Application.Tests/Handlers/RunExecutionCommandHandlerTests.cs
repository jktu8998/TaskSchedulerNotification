using System;
using System.Collections.Generic;
using System.Text.Json;
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
using TaskStatus = Domain.Enums.TaskStatus;

namespace Application.Tests.Handlers;

public class RunExecutionCommandHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IHttpExecutor> _httpExecutorMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly Mock<IDeadLetterRepository> _dlqRepoMock = new();
    private readonly Mock<IRandomProvider> _randomMock = new();
    private readonly RunExecutionCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public RunExecutionCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _randomMock.Setup(r => r.Next(0, 16)).Returns(5); // детерминированный Jitter

        _handler = new RunExecutionCommandHandler(
            _taskRepoMock.Object,
            _httpExecutorMock.Object,
            _unitOfWorkMock.Object,
            _dateTimeMock.Object,
            _dispatcherMock.Object,
            _dlqRepoMock.Object,
            _randomMock.Object
        );
    }

    private ScheduledTask CreateTaskInStatus(
        Guid taskId,
        string senderId = "sender",
        TaskType type = TaskType.OneTime,
        TaskStatus status = TaskStatus.Queued,
        int? timeoutSeconds = null,
        RetryPolicy? retryPolicy = null,
        ResultDeliveryConfig? resultDelivery = null,
        Schedule? schedule = null)
    {
        var task = new ScheduledTask(
            TaskId.From(taskId),
            senderId,
            type,
            schedule ?? Schedule.FromAbsolute(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero)),
            new ExecutionConfig("GET", "https://api.example.com", timeoutSeconds: timeoutSeconds),
            resultDelivery,
            null,
            retryPolicy ?? RetryPolicy.Default,
            null,
            _utcNow);

        // Приводим к нужному статусу
        if (status == TaskStatus.Queued)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
            task.Enqueue(_utcNow);
        }
        else if (status == TaskStatus.Executing)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
            task.Enqueue(_utcNow);
            task.StartExecution(_utcNow, TimeSpan.FromSeconds(30));
        }
        else if (status == TaskStatus.Completed)
        {
            task.ScheduleTask(_utcNow, _utcNow.AddHours(1));
            task.Enqueue(_utcNow);
            task.StartExecution(_utcNow, TimeSpan.FromSeconds(30));
            task.CompleteSuccessfully(_utcNow);
        }
        // Другие статусы можно добавить при необходимости
        return task;
    }

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_OneTimeTask_Success()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTaskInStatus(taskId, status: TaskStatus.Queued);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        var httpResponse = new HttpResponseResult(200, "OK", true);
        _httpExecutorMock.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(httpResponse);

        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(TaskStatus.Completed, capturedTask.Status);
        Assert.Null(capturedTask.LockedUntil);

        // Фазы: захват (одна транзакция), фиксация (вторая транзакция)
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);

        // HttpExecutor вызван один раз
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);

        // События диспатчатся (в фазе захвата и фиксации)
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsNotNull<IReadOnlyCollection<IDomainEvent>>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));

        // DLQ не вызывался
        _dlqRepoMock.Verify(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PeriodicTask_ReschedulesAfterSuccess()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var periodicSchedule = Schedule.FromCron("5 * * * * *", "UTC");
        var task = CreateTaskInStatus(taskId, type: TaskType.Periodic, schedule: periodicSchedule);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        _httpExecutorMock.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseResult(200, null, true));

        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t);

        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(TaskStatus.Scheduled, capturedTask.Status); // перепланировано
        Assert.NotNull(capturedTask.NextExecutionAt);
        Assert.True(capturedTask.NextExecutionAt > _utcNow);
        Assert.Null(capturedTask.LockedUntil);
    }

    [Fact]
    public async Task HandleAsync_HttpFailsWithRetries_SchedulesRetryWithJitter()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTaskInStatus(taskId, status: TaskStatus.Queued);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        _httpExecutorMock.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseResult(500, "Server Error", false));

        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t);

        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(TaskStatus.Scheduled, capturedTask.Status); // Failed -> Scheduled через ScheduleRetry
        // Проверка Jitter: базовый интервал 60 сек + случайный 5 сек
        var expectedRetryTime = _utcNow.AddSeconds(65);
        Assert.Equal(expectedRetryTime, capturedTask.NextExecutionAt);
        Assert.Equal(1, capturedTask.CurrentAttempt);
        Assert.Null(capturedTask.LockedUntil);
        _dlqRepoMock.Verify(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AllRetriesExhausted_MovesToDlq()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var customRetry = new RetryPolicy(new[] { 60 }); // 1 попытка
        var task = CreateTaskInStatus(taskId, retryPolicy: customRetry);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        _httpExecutorMock.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseResult(500, "Error", false));

        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t);

        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);

        // Assert
        Assert.NotNull(capturedTask);
        Assert.Equal(TaskStatus.Dead, capturedTask.Status);

        // Проверяем, что запись в DLQ добавлена
        _dlqRepoMock.Verify(d => d.AddAsync(It.IsAny<DeadLetterEntry>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithResultDelivery_ForwardsResponse()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var resultDelivery = new ResultDeliveryConfig(ResultDeliveryMode.ForwardResponse, "https://callback.example.com", "POST");
        var task = CreateTaskInStatus(taskId, resultDelivery: resultDelivery);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var httpResponse = new HttpResponseResult(200, "Response body", true);
        _httpExecutorMock.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResponse);

        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);

        // Assert: HttpExecutor вызван дважды (выполнение и доставка)
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Exactly(2));
        // Второй вызов — доставка с телом ответа
        _httpExecutorMock.Verify(h => h.ExecuteAsync(
            It.Is<HttpRequestConfig>(c => c.Url == "https://callback.example.com" && c.Body == "Response body"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TaskNotInQueued_Ignores()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTaskInStatus(taskId, status: TaskStatus.Completed); // уже завершено
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);

        // Assert: никаких действий
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    //
    // Негативные тесты  

    [Fact]
    public async Task HandleAsync_TaskNotFound_DoesNothing()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync((ScheduledTask?)null);
    
        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);
    
        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_AlreadyExecuting_Ignores()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTaskInStatus(taskId, status: TaskStatus.Executing);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
    
        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);
    
        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_Phase1CommitFails_RollsBackAndThrows()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTaskInStatus(taskId, status: TaskStatus.Queued);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
    
        _unitOfWorkMock.Setup(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .ThrowsAsync(new Exception("DB commit error"));
    
        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(new RunExecutionCommand(taskId), _ct));
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // HttpExecutor не вызывался
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task HandleAsync_Phase3CommitFails_RollsBackAndThrows()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTaskInStatus(taskId, status: TaskStatus.Queued);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);

        _httpExecutorMock.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseResult(200, null, true));

        // Первый коммит (захват) успешен, второй (фиксация) падает
        _unitOfWorkMock.SetupSequence(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new Exception("Finalization commit error"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(new RunExecutionCommand(taskId), _ct));
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // Диспатч событий был в обеих фазах
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsAny<IReadOnlyCollection<IDomainEvent>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
    
    [Fact]
    public async Task HandleAsync_TaskCancelledAfterPhase1_Phase3ExitsEarly()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var task = CreateTaskInStatus(taskId, status: TaskStatus.Queued);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
    
        // Моделируем, что после захвата задание отменили (вернётся статус не Executing)
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task); // первый вызов для загрузки
        // После того как захват выполнен, перезагружаем задание в фазе 3, возвращаем задание в статусе Cancelled
        var cancelledTask = CreateTaskInStatus(taskId, status: TaskStatus.Queued);
        cancelledTask.Cancel(_utcNow); // статус Cancelled
        _taskRepoMock.SetupSequence(r => r.GetByIdAsync(TaskId.From(taskId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(task)        // для начальной загрузки
            .ReturnsAsync(cancelledTask); // после захвата, перезагрузка
    
        _httpExecutorMock.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseResult(200, null, true));
    
        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);
    
        // Assert: транзакция была только одна (захвата), фиксация результата не выполнялась
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // Http executor всё равно вызывался (фаза 2)
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    
    [Fact]
    public async Task HandleAsync_ResultDeliveryFails_DoesNotAffectTaskCompletion()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var resultDelivery = new ResultDeliveryConfig(ResultDeliveryMode.ForwardResponse, "https://callback.example.com", "POST");
        var task = CreateTaskInStatus(taskId, resultDelivery: resultDelivery);
        _taskRepoMock.Setup(r => r.GetByIdAsync(TaskId.From(taskId), It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(task);
    
        // Первый вызов (выполнение) успешен, второй (доставка) падает
        _httpExecutorMock.SetupSequence(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseResult(200, "ok", true))
            .ThrowsAsync(new Exception("Delivery failed"));
    
        ScheduledTask? capturedTask = null;
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t);
    
        // Act
        await _handler.HandleAsync(new RunExecutionCommand(taskId), _ct);
    
        // Assert: задание всё равно Completed
        Assert.NotNull(capturedTask);
        Assert.Equal(TaskStatus.Completed, capturedTask.Status);
        // Http executor вызывался дважды
        _httpExecutorMock.Verify(h => h.ExecuteAsync(It.IsAny<HttpRequestConfig>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
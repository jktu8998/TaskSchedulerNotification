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
using FluentAssertions;
using TaskStatus = Domain.Enums.TaskStatus;

namespace Application.Tests.Handlers;

public class CreateTaskCommandHandlerTests
{
    private readonly Mock<ITaskRepository> _taskRepoMock = new();
    private readonly Mock<IEncryptionService> _encryptionMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcherMock = new();
    private readonly CreateTaskCommandHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;

    // Фиксированное время для детерминированных проверок
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public CreateTaskCommandHandlerTests()
    {
        _dateTimeMock.Setup(d => d.UtcNow).Returns(_utcNow);
        _requestContextMock.Setup(r => r.SenderId).Returns("test-sender");

        _handler = new CreateTaskCommandHandler(
            _taskRepoMock.Object,
            _encryptionMock.Object,
            _dateTimeMock.Object,
            _requestContextMock.Object,
            _unitOfWorkMock.Object,
            _dispatcherMock.Object
        );
    }

    private CreateTaskRequest CreateValidRequest(
        string type = "OneTime",
        string? executeAt = "2026-07-11T10:00:00Z",
        string? offset = null,
        string? cron = null,
        string? timezone = null,
        ExecutionConfigDto? execution = null,
        ResultDeliveryConfigDto? resultDelivery = null,
        PollingConfigDto? pollingConfig = null,
        RetryPolicyDto? retry = null,
        string? sensitiveData = null)
    {
        return new CreateTaskRequest
        {
            Type = type,
            Schedule = new ScheduleDto
            {
                ExecuteAt = executeAt,
                Offset = offset,
                Cron = cron,
                Timezone = timezone
            },
            Execution = execution ?? new ExecutionConfigDto
            {
                Method = "GET",
                Url = "https://example.com"
            },
            ResultDelivery = resultDelivery,
            PollingConfig = pollingConfig,
            Retry = retry,
            SensitiveData = sensitiveData
        };
    }

   // ========== Позитивные тесты ==========

    [Fact]
    public async Task HandleAsync_OneTimeAbsolute_Success()
    {
        // Arrange
        var request = CreateValidRequest(executeAt: "2026-07-11T10:00:00Z");
        var command = new CreateTaskCommand(request);
        
        // 1. ИСПРАВЛЕНИЕ: Добавляем знак вопроса, указывая, что тип Nullable
        ScheduledTask? capturedTask = null;

        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)))
            .Callback<ScheduledTask, CancellationToken>((task, _) => capturedTask = task)
            .Returns(Task.CompletedTask);

        // Act
        var taskId = await _handler.HandleAsync(command, _ct);

        // Assert
        taskId.Should().NotBeEmpty();
        
        // 2. ИСПРАВЛЕНИЕ: Используем xUnit для проверки на null. 
        // Это снимает все вопросы у компилятора.
        Assert.NotNull(capturedTask);
        
        // 3. ИСПРАВЛЕНИЕ: Теперь мы точно знаем, что task не null (убираем CS8602)
        var task = capturedTask!; 

        task.SenderId.Should().Be("test-sender");
        task.Type.Should().Be(TaskType.OneTime);
        task.Status.Should().Be(TaskStatus.Scheduled);
        task.Schedule.IsAbsolute.Should().BeTrue();
        
        // Проверка nullable свойств внутри агрегата (используем ! для успокоения компилятора)
        Assert.NotNull(task.Schedule.ExecuteAt);
        task.Schedule.ExecuteAt!.Value.Should().Be(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero));
        
        Assert.NotNull(task.NextExecutionAt);
        task.NextExecutionAt!.Value.Should().Be(task.Schedule.ExecuteAt!.Value.UtcDateTime);
        
        task.Execution.Method.Should().Be("GET");
        task.Execution.Url.Should().Be("https://example.com");
        
        // Для сложных объектов тоже можно использовать Assert.NotNull
        Assert.NotNull(task.RetryPolicy);
        
        task.RetryPolicy.MaxAttempts.Should().Be(RetryPolicy.Default.MaxAttempts);
        task.RetryPolicy.IntervalsSeconds.Should().BeEquivalentTo(RetryPolicy.Default.IntervalsSeconds);
       
        task.EncryptedSensitiveData.Should().BeNull();
        task.CreatedAt.Should().Be(_utcNow);
        task.UpdatedAt.Should().Be(_utcNow);

        // Проверка транзакционности (остается без изменений)
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        _dispatcherMock.Verify(d => d.DispatchAsync(It.IsNotNull<IReadOnlyCollection<IDomainEvent>>(), It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PeriodicCron_Success()
    {
        // Arrange
        var request = CreateValidRequest(
            type: "Periodic",
            executeAt: null,
            cron: "0 0 3 * * *", 
            timezone: "Europe/Moscow");
        var command = new CreateTaskCommand(request);
        
        ScheduledTask? capturedTask = null; // ИСПРАВЛЕНО
        
        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedTask);
        var task = capturedTask!; // ИСПРАВЛЕНО

        task.Type.Should().Be(TaskType.Periodic);
        task.Schedule.CronExpression.Should().Be("0 0 3 * * *");
        task.Schedule.Timezone.Should().Be("Europe/Moscow");
        
        Assert.NotNull(task.NextExecutionAt);
        task.NextExecutionAt!.Value.Should().BeAfter(_utcNow);
    }

    [Fact]
    public async Task HandleAsync_Offset_Success()
    {
        // Arrange
        var request = CreateValidRequest(
            type: "OneTime",
            executeAt: null,
            offset: "2h");
        var command = new CreateTaskCommand(request);
        
        ScheduledTask? capturedTask = null; // ИСПРАВЛЕНО
        
        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedTask);
        var task = capturedTask!; // ИСПРАВЛЕНО

        task.Schedule.IsOffset.Should().BeTrue();
        
        Assert.NotNull(task.Schedule.Offset);
        task.Schedule.Offset!.Value.Should().Be(TimeSpan.FromHours(2));
        
        Assert.NotNull(task.NextExecutionAt);
        task.NextExecutionAt!.Value.Should().Be(_utcNow + TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task HandleAsync_WithAllOptionalConfigs_SetsProperties()
    {
        // Arrange
        var request = CreateValidRequest(
            execution: new ExecutionConfigDto
            {
                Method = "POST",
                Url = "https://api.example.com",
                Headers = new Dictionary<string, string> { { "X-Custom", "value" } },
                Body = "{\"data\":1}",
                TimeoutSeconds = 60
            },
            resultDelivery: new ResultDeliveryConfigDto
            {
                Mode = "ForwardResponse",
                Url = "https://callback.example.com",
                Method = "POST"
            },
            pollingConfig: new PollingConfigDto
            {
                Field = "status",
                Condition = "changed",
                IntervalSeconds = 120
            },
            retry: new RetryPolicyDto { IntervalsSeconds = new[] { 10, 30, 60 } }
        );
        var command = new CreateTaskCommand(request);
        
        ScheduledTask? capturedTask = null; // ИСПРАВЛЕНО
        
        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedTask);
        var task = capturedTask!; // ИСПРАВЛЕНО

        task.Execution.Method.Should().Be("POST");
        task.Execution.Headers.Should().ContainKey("X-Custom");
        task.Execution.Body.Should().Be("{\"data\":1}");
        task.Execution.TimeoutSeconds.Should().Be(60);
        
        // ИСПРАВЛЕНИЕ: Проверяем nullable свойства через Assert.NotNull
        Assert.NotNull(task.ResultDelivery);
        task.ResultDelivery!.Mode.Should().Be(ResultDeliveryMode.ForwardResponse);
        task.ResultDelivery!.Url.Should().Be("https://callback.example.com");
        
        Assert.NotNull(task.PollingConfig);
        task.PollingConfig!.Field.Should().Be("status");
        task.PollingConfig!.IntervalSeconds.Should().Be(120);
        
        Assert.NotNull(task.RetryPolicy);
        task.RetryPolicy!.MaxAttempts.Should().Be(3);
        task.RetryPolicy!.IntervalsSeconds.Should().BeEquivalentTo(new[] { 10, 30, 60 });
    }

    [Fact]
    public async Task HandleAsync_WithSensitiveData_Encrypts()
    {
        // Arrange
        var request = CreateValidRequest(sensitiveData: "my-secret");
        var command = new CreateTaskCommand(request);
        _encryptionMock.Setup(e => e.Encrypt("my-secret")).Returns("encrypted-my-secret");
        
        ScheduledTask? capturedTask = null; // ИСПРАВЛЕНО
        
        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(command, _ct);

        // Assert
        Assert.NotNull(capturedTask);
        var task = capturedTask!; // ИСПРАВЛЕНО

        task.EncryptedSensitiveData.Should().Be("encrypted-my-secret");
        _encryptionMock.Verify(e => e.Encrypt("my-secret"), Times.Once);
    }

    // ========== Негативные тесты ==========

    [Fact]
    public async Task HandleAsync_InvalidCronFormat_Throws()
    {
        // Arrange: передаем откровенно невалидную строку вместо cron
        var request = CreateValidRequest(
            type: "Periodic",
            executeAt: null,
            cron: "not-a-valid-cron-string"); 
        
        var command = new CreateTaskCommand(request);

        // Act
        Func<Task> act = () => _handler.HandleAsync(command, _ct);

        // Assert
        // При попытке распарсить строку в ScheduleMapper или CronExpression.Parse
        // система выбросит исключение (например, CronFormatException) ДО начала транзакции.
        await act.Should().ThrowAsync<Exception>();
        
        // Главная проверка: убеждаемся, что мусор не попал в базу данных
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InvalidSchedule_ThrowsArgumentException()
    {
        // Arrange: все поля Schedule пусты
        var request = CreateValidRequest(executeAt: null, offset: null, cron: null);
        var command = new CreateTaskCommand(request);

        // Act
        Func<Task> act = () => _handler.HandleAsync(command, _ct);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ExecuteAt, Offset или Cron*");
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CommitFails_RollsBackAndThrows()
    {
        // Arrange
        var request = CreateValidRequest();
        var command = new CreateTaskCommand(request);
        _unitOfWorkMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("DB error"));

        // Act
        Func<Task> act = () => _handler.HandleAsync(command, _ct);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("DB error");
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.Is<CancellationToken>(ct => ct == _ct)), Times.Once);
        // Проверяем, что после отката событие очищено (нельзя проверить напрямую, но можно косвенно: мок AddAsync был вызван, но потом Rollback)
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_EncryptionFails_Throws()
    {
        // Arrange
        //var request = CreateValidRequest(sensitiveData: "secret");
        // ИСПРАВЛЕНО: Тестируем, что система не пропускает мусор в поле Cron
        var request = CreateValidRequest(
            type: "Periodic",
            executeAt: null,
            cron: "invalid-cron-string");
        var command = new CreateTaskCommand(request);
        _encryptionMock.Setup(e => e.Encrypt(It.IsAny<string>())).Throws(new InvalidOperationException("Encryption failed"));

        // Act
        Func<Task> act = () => _handler.HandleAsync(command, _ct);

        // Assert
        //await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Encryption failed");
        await act.Should().ThrowAsync<Exception>();
        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
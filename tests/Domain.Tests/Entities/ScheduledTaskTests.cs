using System;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Xunit;
using FluentAssertions;

namespace Domain.Tests.Entities;

public class ScheduledTaskTests
{
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Arrange
        var taskId = TaskId.New();
        var senderId = "service-123";
        var schedule = Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1));
        var execution = new ExecutionConfig("GET", "https://api.example.com");
        var utcNow = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var task = new ScheduledTask(
            taskId, senderId, TaskType.OneTime, schedule, execution,
            resultDelivery: null, pollingConfig: null, retryPolicy: null,
            encryptedSensitiveData: null, utcNow);

        // Assert
        Assert.Equal(taskId, task.Id);
        Assert.Equal(senderId, task.SenderId);
        Assert.Equal(TaskType.OneTime, task.Type);
        Assert.Equal(StatusTask.Created, task.Status);

        // Schedule — record с простыми полями, сравнение по значению сработает
        Assert.Equal(schedule, task.Schedule);
        Assert.Null(task.ResultDelivery);
        Assert.Null(task.PollingConfig);

        // ExecutionConfig: проверяем отдельные поля, т.к. Headers — ссылочный словарь
        Assert.Equal("GET", task.Execution.Method);
        Assert.Equal("https://api.example.com", task.Execution.Url);
        Assert.Empty(task.Execution.Headers);
        Assert.Null(task.Execution.Body);

        // RetryPolicy: сравниваем интервалы через SequenceEqual, а не сам объект
        Assert.NotNull(task.RetryPolicy);
        Assert.Equal(RetryPolicy.Default.MaxAttempts, task.RetryPolicy.MaxAttempts);
        Assert.True(RetryPolicy.Default.IntervalsSeconds
            .SequenceEqual(task.RetryPolicy.IntervalsSeconds));

        Assert.Null(task.EncryptedSensitiveData);
        Assert.Equal(utcNow, task.CreatedAt);
        Assert.Equal(utcNow, task.UpdatedAt);
        Assert.Equal(0, task.CurrentAttempt);
        Assert.Null(task.LockedUntil);
        Assert.Null(task.NextExecutionAt);
    }
    
    [Fact]
    public void ScheduleTask_FromCreated_TransitionsToScheduled()
    {
        // Arrange
        var task = CreateValidTask();
        var scheduledTime = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

        // Act
        task.ScheduleTask(scheduledTime, scheduledTime);

        // Assert
        task.Status.Should().Be(StatusTask.Scheduled);
        task.NextExecutionAt.Should().Be(scheduledTime);
        task.DomainEvents.Should().ContainSingle(e => e is TaskScheduledEvent);
    }

// Вспомогательный метод для создания валидного задания в статусе Created
    private ScheduledTask CreateValidTask()
    {
        var utcNow = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        return new ScheduledTask(
            TaskId.New(),
            "service-test",
            TaskType.OneTime,
            Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
            new ExecutionConfig("GET", "https://test.com"),
            null, null, null, null,
            utcNow);
    }
    
    [Fact]
    public void Enqueue_FromScheduled_TransitionsToQueued()
    {
        // Arrange
        var task = CreateValidTask();
        var scheduledTime = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(scheduledTime, scheduledTime);
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ClearDomainEvents(); // <--- Очищаем события от предыдущих шагов!
        // Act
        task.Enqueue(utcNow);

        // Assert
        task.Status.Should().Be(StatusTask.Queued);
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskQueuedEvent);
    }
    
    [Fact]
    public void ScheduleTask_FromQueued_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var scheduledTime = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(scheduledTime, scheduledTime);
        task.Enqueue(scheduledTime); // теперь статус Queued

        // Act
        var act = () => task.ScheduleTask(scheduledTime, scheduledTime);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot schedule task*");
    }
    [Fact]
    public void ScheduleTask_FromCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var scheduledTime = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(scheduledTime, scheduledTime);
        task.Enqueue(scheduledTime);
        task.StartExecution(scheduledTime, TimeSpan.FromSeconds(30));
        task.CompleteSuccessfully(scheduledTime); // статус Completed

        // Act
        var act = () => task.ScheduleTask(scheduledTime, scheduledTime);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot schedule task*");
    }
    
    [Fact]
    public void Enqueue_FromCreated_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask(); // статус Created

        // Act
        var act = () => task.Enqueue(DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot enqueue task*");
    }
    [Fact]
    public void Enqueue_FromExecuting_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30)); // статус Executing

        // Act
        var act = () => task.Enqueue(utcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot enqueue task*");
    }
    [Fact]
    public void StartExecution_FromQueued_TransitionsToExecuting()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        var lockDuration = TimeSpan.FromSeconds(30);

        // Act
        task.StartExecution(utcNow, lockDuration);

        // Assert
        task.Status.Should().Be(StatusTask.Executing);
        task.LockedUntil.Should().Be(utcNow + lockDuration);
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskExecutionStartedEvent);
    }
    
    [Fact]
    public void StartExecution_FromScheduled_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);

        // Act
        var act = () => task.StartExecution(utcNow, TimeSpan.FromSeconds(30));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot start execution*");
    }
    
    [Fact]
    public void CompleteSuccessfully_FromExecuting_TransitionsToCompleted()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30));

        // Act
        task.CompleteSuccessfully(utcNow);

        // Assert
        task.Status.Should().Be(StatusTask.Completed);
        task.LockedUntil.Should().BeNull();
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskCompletedEvent);
    }
    [Fact]
    public void CompleteSuccessfully_FromScheduled_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);

        // Act
        var act = () => task.CompleteSuccessfully(utcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot complete task*");
    }
    [Fact]
    public void CompleteSuccessfully_FromCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30));
        task.CompleteSuccessfully(utcNow);

        // Act
        var act = () => task.CompleteSuccessfully(utcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot complete task*");
    }
    
    //
    [Fact]
    public void MarkFailed_WhenRetriesRemain_TransitionsToFailedAndIncrementsAttempt()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30));

        // Act
        task.MarkFailed(utcNow, "Test error");

        // Assert
        task.Status.Should().Be(StatusTask.Failed);
        task.CurrentAttempt.Should().Be(1); // было 0, стало 1
        task.LockedUntil.Should().BeNull(); // блокировка снята
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskFailedEvent);
    }
    /*[Fact]
    public void MarkFailed_WhenAllRetriesExhausted_TransitionsToDead()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30));

        // Act: исчерпываем все 5 попыток (CurrentAttempt растёт с 0 до 5)
        for (int i = 0; i < RetryPolicy.Default.MaxAttempts; i++)
        {
            task.MarkFailed(utcNow, "Error");
        }

        // Assert
        task.Status.Should().Be(StatusTask.Dead);
        task.CurrentAttempt.Should().Be(RetryPolicy.Default.MaxAttempts); // 5
        task.LockedUntil.Should().BeNull();
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskMovedToDlqEvent);
    }*/
    
    [Fact]
    public void MarkFailed_WhenAllRetriesExhausted_TransitionsToDead()
    {
        // Arrange
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        var customRetry = new RetryPolicy(new[] { 60, 60 }); // всего 2 попытки
        var task = new ScheduledTask(
            TaskId.New(),
            "service-test",
            TaskType.OneTime,
            Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
            new ExecutionConfig("GET", "https://test.com"),
            null, null, customRetry, null,
            utcNow);

        // Первый проход: Schedule -> Queued -> Executing -> MarkFailed (Failed)
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30));
        task.MarkFailed(utcNow, "Error");

        // Assert после первого падения
        task.Status.Should().Be(StatusTask.Failed);
        task.CurrentAttempt.Should().Be(1);

        // Ретрай: переводим обратно в Scheduled -> Queued -> Executing
        task.ScheduleRetry(utcNow, utcNow.AddSeconds(60));
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30));

        // Второе падение: исчерпывает попытки
        task.MarkFailed(utcNow, "Error");

        // Assert
        task.Status.Should().Be(StatusTask.Dead);
        task.CurrentAttempt.Should().Be(2); // обе попытки использованы
        task.LockedUntil.Should().BeNull();
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskMovedToDlqEvent);
    }
    
    [Fact]
    public void Pause_FromScheduled_TransitionsToPaused()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);

        // Act
        task.Pause(utcNow);

        // Assert
        task.Status.Should().Be(StatusTask.Paused);
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskPausedEvent);
    }
    [Fact]
    public void Pause_FromCreated_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask(); // статус Created

        // Act
        var act = () => task.Pause(DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot pause task*");
    }
    [Fact]
    public void Resume_FromPaused_TransitionsToScheduled()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Pause(utcNow);

        // Act
        task.Resume(utcNow);

        // Assert
        task.Status.Should().Be(StatusTask.Scheduled);
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskResumedEvent);
    }
    [Fact]
    public void Resume_FromScheduled_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);

        // Act
        var act = () => task.Resume(utcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot resume task*");
    }
    
    //
    [Fact]
    public void Cancel_FromScheduled_TransitionsToCancelled()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);

        // Act
        task.Cancel(utcNow);

        // Assert
        task.Status.Should().Be(StatusTask.Cancelled);
        task.UpdatedAt.Should().Be(utcNow);
        task.DomainEvents.Should().ContainSingle(e => e is TaskCancelledEvent);
    }
    
    [Fact]
    public void Cancel_FromCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var task = CreateValidTask();
        var utcNow = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
        task.ScheduleTask(utcNow, utcNow);
        task.Enqueue(utcNow);
        task.StartExecution(utcNow, TimeSpan.FromSeconds(30));
        task.CompleteSuccessfully(utcNow); // статус Completed

        // Act
        var act = () => task.Cancel(utcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot cancel task*");
    }
}
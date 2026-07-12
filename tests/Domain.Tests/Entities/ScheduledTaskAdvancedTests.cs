using System;
using System.Linq;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Xunit;

namespace Domain.Tests.Entities;

public class ScheduledTaskAdvancedTests
{
    // Вспомогательный метод для создания задания в нужном статусе
    private ScheduledTask CreateTaskInStatus(StatusTask target, out DateTime utcNow, 
        RetryPolicy? retryPolicy = null, TaskType type = TaskType.OneTime)
    {
        utcNow = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var task = new ScheduledTask(
            TaskId.New(),
            "service-test",
            type,
            Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
            new ExecutionConfig("GET", "https://test.com"),
            null, null, retryPolicy, null,
            utcNow);

        switch (target)
        {
            case StatusTask.Created:
                return task;
            case StatusTask.Scheduled:
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                break;
            case StatusTask.Queued:
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                task.Enqueue(utcNow);
                break;
            case StatusTask.Executing:
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                task.Enqueue(utcNow);
                task.StartExecution(utcNow, TimeSpan.FromSeconds(30));
                break;
            case StatusTask.Completed:
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                task.Enqueue(utcNow);
                task.StartExecution(utcNow, TimeSpan.FromSeconds(30));
                task.CompleteSuccessfully(utcNow);
                break;
            case StatusTask.Failed:
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                task.Enqueue(utcNow);
                task.StartExecution(utcNow, TimeSpan.FromSeconds(30));
                task.MarkFailed(utcNow);
                break;
            case StatusTask.Dead:
                // используем кастомный retry с 1 попыткой
                var oneAttemptRetry = new RetryPolicy(new[] { 60 });
                task = new ScheduledTask(
                    TaskId.New(), "service-test", type,
                    Schedule.FromAbsolute(DateTimeOffset.UtcNow.AddHours(1)),
                    new ExecutionConfig("GET", "https://test.com"),
                    null, null, oneAttemptRetry, null, utcNow);
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                task.Enqueue(utcNow);
                task.StartExecution(utcNow, TimeSpan.FromSeconds(30));
                task.MarkFailed(utcNow);
                break;
            case StatusTask.Paused:
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                task.Pause(utcNow);
                break;
            case StatusTask.Cancelled:
                task.ScheduleTask(utcNow, utcNow.AddHours(1));
                task.Cancel(utcNow);
                break;
        }
        task.ClearDomainEvents();
        return task;
    }

    #region Reschedule tests
    [Fact]
    public void Reschedule_FromExecuting_TransitionsToScheduled()
    {
        var task = CreateTaskInStatus(StatusTask.Executing, out var utcNow);
        var nextTime = utcNow.AddHours(2);
        task.Reschedule(utcNow, nextTime);

        Assert.Equal(StatusTask.Scheduled, task.Status);
        Assert.Equal(nextTime, task.NextExecutionAt);
        Assert.Null(task.LockedUntil);
        Assert.Single(task.DomainEvents, e => e is TaskScheduledEvent);
    }

    [Fact]
    public void Reschedule_FromNonExecuting_Throws()
    {
        foreach (StatusTask status in Enum.GetValues(typeof(StatusTask)))
        {
            if (status == StatusTask.Executing) continue;
            if (status == StatusTask.Completed || status == StatusTask.Dead || status == StatusTask.Cancelled)
                continue; // нельзя создать задание в этих статусах через вспомогательный метод, кроме Executing и т.д.
            var task = CreateTaskInStatus(status, out var utcNow);
            Assert.Throws<InvalidOperationException>(() => task.Reschedule(utcNow, utcNow.AddHours(1)));
        }
    }
    #endregion

    #region ScheduleRetry tests
    [Fact]
    public void ScheduleRetry_FromFailed_TransitionsToScheduled()
    {
        var task = CreateTaskInStatus(StatusTask.Failed, out var utcNow);
        var nextAttemptTime = utcNow.AddSeconds(60);
        task.ScheduleRetry(utcNow, nextAttemptTime);

        Assert.Equal(StatusTask.Scheduled, task.Status);
        Assert.Equal(nextAttemptTime, task.NextExecutionAt);
        Assert.Null(task.LockedUntil);
        Assert.Single(task.DomainEvents, e => e is TaskScheduledEvent);
    }

    [Fact]
    public void ScheduleRetry_FromNonFailed_Throws()
    {
        foreach (StatusTask status in Enum.GetValues(typeof(StatusTask)))
        {
            if (status == StatusTask.Failed) continue;
            var task = CreateTaskInStatus(status, out var utcNow);
            Assert.Throws<InvalidOperationException>(() => task.ScheduleRetry(utcNow, utcNow.AddSeconds(60)));
        }
    }
    #endregion

    #region ClearDomainEvents
    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var task = CreateTaskInStatus(StatusTask.Scheduled, out var utcNow);
        task.Pause(utcNow);
        Assert.NotEmpty(task.DomainEvents);
        task.ClearDomainEvents();
        Assert.Empty(task.DomainEvents);
    }
    #endregion

    #region Cancel from Executing
    [Fact]
    public void Cancel_FromExecuting_ResetsLockedUntil()
    {
        var task = CreateTaskInStatus(StatusTask.Executing, out var utcNow);
        Assert.NotNull(task.LockedUntil);
        task.Cancel(utcNow);
        Assert.Equal(StatusTask.Cancelled, task.Status);
        Assert.Null(task.LockedUntil);
        Assert.Single(task.DomainEvents, e => e is TaskCancelledEvent);
    }
    #endregion
}
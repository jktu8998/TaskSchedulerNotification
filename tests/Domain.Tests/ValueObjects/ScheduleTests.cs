using System;
using Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Domain.Tests.ValueObjects;

public class ScheduleTests
{
    [Fact]
    public void GetNextOccurrence_Absolute_ReturnsExactTime()
    {
        // Arrange
        var exactTime = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var schedule = Schedule.FromAbsolute(exactTime);

        // Act
        var next = schedule.GetNextOccurrence(DateTime.UtcNow);

        // Assert
        next.Should().Be(exactTime.UtcDateTime);
    }
    
    [Fact]
    public void GetNextOccurrence_Offset_AddsOffsetToBaseTime()
    {
        // Arrange
        var offset = TimeSpan.FromMinutes(30);
        var schedule = Schedule.FromOffset(offset);
        var baseTime = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var next = schedule.GetNextOccurrence(baseTime);

        // Assert
        next.Should().Be(baseTime + offset);
    }
    
    [Fact]
    public void GetNextOccurrence_Cron_ReturnsNextOccurrence()
    {
        // Arrange: ежедневно в 3:00 по UTC
        var schedule = Schedule.FromCron("0 0 3 * * *", "UTC");
        var baseTime = new DateTime(2026, 7, 10, 2, 0, 0, DateTimeKind.Utc);

        // Act
        var next = schedule.GetNextOccurrence(baseTime);

        // Assert: ближайшее вхождение после 2:00 — сегодня в 3:00
        next.Should().Be(new DateTime(2026, 7, 10, 3, 0, 0, DateTimeKind.Utc));
    }
    
    [Fact]
    public void GetNextOccurrence_Cron_WhenBaseTimeAfterTrigger_ReturnsNextDay()
    {
        // Arrange: ежедневно в 3:00 UTC
        var schedule = Schedule.FromCron("0 0 3 * * *", "UTC");
        var baseTime = new DateTime(2026, 7, 10, 4, 0, 0, DateTimeKind.Utc); // уже позже 3:00

        // Act
        var next = schedule.GetNextOccurrence(baseTime);

        // Assert: сегодня поезд ушёл, ближайшее — завтра в 3:00
        next.Should().Be(new DateTime(2026, 7, 11, 3, 0, 0, DateTimeKind.Utc));
    }
}
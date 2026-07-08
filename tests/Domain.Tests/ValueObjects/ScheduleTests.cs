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
    [Fact]
    public void GetNextOccurrence_CronWithMoscowTimezone_ReturnsCorrectUtcTime()
    {
        // Arrange: ежедневно в 3:00 по Москве (UTC+3 без учёта перехода, фиксированная дата)
        // Для простоты используем дату, когда летнее время не влияет, или учтём что zone id корректный.
        var schedule = Schedule.FromCron("0 0 3 * * *", "Europe/Moscow");
        // Задаём базовое время UTC, соответствующее 2:00 МСК (UTC-1) → в Москве 4:00? Нужно чётко посчитать.
        // Возьмём базовое время 2026-01-01 01:00:00 UTC, что соответствует 04:00 МСК (UTC+3). Следующее 3:00 МСК будет завтра.
        var baseTimeUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc); // 01:00 UTC → 04:00 МСК
        // Следующее 3:00 МСК наступит 2026-01-02 00:00:00 UTC (3:00 - 3 часа = 00:00 UTC)
        var expectedNextUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var next = schedule.GetNextOccurrence(baseTimeUtc);

        // Assert
        next.Should().Be(expectedNextUtc);
    }
    [Fact]
    public void GetNextOccurrence_WhenNoValidSpecification_ThrowsInvalidOperationException()
    {
        // Arrange: Создаем объект в обход конструкторов (все поля останутся null)
        var schedule = (Schedule)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Schedule));

        // Act
        Action act = () => schedule.GetNextOccurrence(DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no valid specification*");
    }
}
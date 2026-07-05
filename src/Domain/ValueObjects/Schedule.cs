using System;

namespace Domain.ValueObjects;

/// <summary>
/// Расписание выполнения задания.
/// Поддерживает три взаимоисключающих режима:
/// - абсолютное время (ExecuteAt)
/// - смещение от текущего времени (Offset)
/// - cron-выражение с таймзоной (CronExpression + Timezone)
/// Создаётся только через статические фабрики, гарантирующие валидность.
/// Неизменяемый, сравнивается по значению.
/// </summary>
public sealed record Schedule
{
    /// <summary>Абсолютное время выполнения (ISO 8601).</summary>
    public DateTimeOffset? ExecuteAt { get; init; }

    /// <summary>Смещение от текущего времени (5m, 1h, 2d).</summary>
    public TimeSpan? Offset { get; init; }

    /// <summary>Cron-выражение для периодических заданий.</summary>
    public string? CronExpression { get; init; }

    /// <summary>Таймзона для cron (IANA, например "Europe/Moscow").</summary>
    public string? Timezone { get; init; }

    // Приватный конструктор — только фабрики могут создавать объект.
    private Schedule(DateTimeOffset? executeAt, TimeSpan? offset, string? cron, string? timezone)
    {
        ExecuteAt = executeAt;
        Offset = offset;
        CronExpression = cron;
        Timezone = timezone;
    }

    /// <summary>Создаёт расписание на конкретное время.</summary>
    public static Schedule FromAbsolute(DateTimeOffset executeAt)
        => new(executeAt, null, null, null);

    /// <summary>Создаёт расписание со смещением от текущего времени.</summary>
    public static Schedule FromOffset(TimeSpan offset)
        => new(null, offset, null, null);

    /// <summary>Создаёт cron-расписание с таймзоной.</summary>
    public static Schedule FromCron(string cronExpression, string timezone)
        => new(null, null, cronExpression, timezone);

    // Вспомогательные свойства для быстрой проверки типа расписания.
    public bool IsAbsolute => ExecuteAt.HasValue;
    public bool IsOffset => Offset.HasValue;
    public bool IsCron => !string.IsNullOrWhiteSpace(CronExpression);

    public override string ToString() =>
        IsAbsolute ? $"At {ExecuteAt:O}" :
        IsOffset ? $"After {Offset}" :
        IsCron ? $"Cron '{CronExpression}' ({Timezone})" : "Unknown";
}
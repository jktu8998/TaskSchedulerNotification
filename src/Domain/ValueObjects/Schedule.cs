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
    
    /// <summary>
    /// Вычисляет абсолютное время следующего выполнения на основе базового времени.
    /// Для Absolute возвращает само ExecuteAt.
    /// Для Offset прибавляет смещение к baseTime.
    /// Для Cron находит ближайшее вхождение после baseTime (или в момент baseTime, если совпадает).
    /// Если cron не имеет будущих вхождений, возвращает null.
    /// </summary>
    /// <param name="baseTime">Базовое время в UTC.</param>
    /// <returns>Следующее время выполнения или null.</returns>
    public DateTime? GetNextOccurrence(DateTime baseTime)
    {
        if (IsAbsolute)
            return ExecuteAt!.Value.UtcDateTime;

        if (IsOffset)
            return baseTime + Offset!.Value;

        if (IsCron)
        {
            var cron = Cronos.CronExpression.Parse(this.CronExpression, Cronos.CronFormat.IncludeSeconds);
        
            // Безопасное получение таймзоны с фоллбеком на UTC
            TimeZoneInfo timeZone = TimeZoneInfo.Utc;
            if (!string.IsNullOrWhiteSpace(Timezone))
            {
                if (!TimeZoneInfo.TryFindSystemTimeZoneById(Timezone, out timeZone))
                {
                    // Если таймзона не найдена, оставляем UTC (продакшен не упадёт)
                    // TODO: залогировать предупреждение через доменное событие или ILogger (но не здесь)
                    timeZone = TimeZoneInfo.Utc;
                }
            }
        
            return cron.GetNextOccurrence(baseTime, timeZone, true);
        }

        throw new InvalidOperationException("Schedule has no valid specification.");
    }

    public override string ToString() =>
        IsAbsolute ? $"At {ExecuteAt:O}" :
        IsOffset ? $"After {Offset}" :
        IsCron ? $"Cron '{CronExpression}' ({Timezone})" : "Unknown";
}
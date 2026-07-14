using Cronos;

namespace Domain.ValueObjects;

/// <summary>
/// Расписание выполнения задания.
/// Поддерживает три взаимоисключающих режима:
/// - абсолютное время (ExecuteAt)
/// - смещение от текущего времени (Offset)
/// - cron-выражение с таймзоной (CronExpression + Timezone)
/// Создаётся только через статические фабрики, гарантирующие валидность.
/// Неизменяемый, сравнивается по бизнес-свойствам (кеш парсера исключён).
/// </summary>
public sealed record Schedule
{
    public DateTimeOffset? ExecuteAt { get; init; }
    public TimeSpan? Offset { get; init; }
    public string? CronExpression { get; init; }
    public string? Timezone { get; init; }

    // Кеш скомпилированного cron-парсера. Заполняется лениво при первом вызове GetNextOccurrence.
    // Не участвует в сравнении record.
    private CronExpression? _parsedCron;

    // Пустой конструктор для Dapper (без валидации, без парсинга)
    private Schedule() { }

    // Приватный конструктор — только фабрики
    private Schedule(
        DateTimeOffset? executeAt,
        TimeSpan? offset,
        string? cron,
        string? timezone)
    {
        // Взаимоисключаемость: ровно один режим должен быть задан
        int modesCount = 0;
        if (executeAt.HasValue) modesCount++;
        if (offset.HasValue) modesCount++;
        if (!string.IsNullOrWhiteSpace(cron)) modesCount++;

        if (modesCount != 1)
            throw new ArgumentException(
                "Exactly one schedule mode must be specified: absolute, offset, or cron.");

        // Валидация offset
        if (offset.HasValue && offset.Value <= TimeSpan.Zero)
            throw new ArgumentException("Offset must be positive.", nameof(offset));

        // Валидация cron + таймзоны (парсинг не здесь, а лениво в GetNextOccurrence)
        if (!string.IsNullOrWhiteSpace(cron))
        {
            // Проверим, что cron парсится, но результат не сохраняем — чтобы не нарушать сравнение и Dapper
            _ = Cronos.CronExpression.Parse(cron, CronFormat.IncludeSeconds);

            if (string.IsNullOrWhiteSpace(timezone))
                throw new ArgumentException("Timezone is required for cron schedule.", nameof(timezone));
        }

        // Если timezone задана, проверить её существование
        if (!string.IsNullOrWhiteSpace(timezone))
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
                throw new ArgumentException(
                    $"Timezone '{timezone}' is not a valid IANA timezone on this system.", nameof(timezone));
        }

        ExecuteAt = executeAt;
        Offset = offset;
        CronExpression = cron;
        Timezone = timezone;
        // _parsedCron оставляем null — заполнится лениво
    }

    // Статические фабрики (без изменений)
    public static Schedule FromAbsolute(DateTimeOffset executeAt)
        => new(executeAt, null, null, null);

    public static Schedule FromOffset(TimeSpan offset)
        => new(null, offset, null, null);

    public static Schedule FromCron(string cronExpression, string timezone)
        => new(null, null, cronExpression, timezone);

    public bool IsAbsolute => ExecuteAt.HasValue;
    public bool IsOffset => Offset.HasValue;
    public bool IsCron => !string.IsNullOrWhiteSpace(CronExpression);

    /// <summary>
    /// Вычисляет следующее время выполнения.
    /// При первом вызове для cron-режима парсит выражение и кеширует парсер.
    /// </summary>
    public DateTime? GetNextOccurrence(DateTime baseTime)
    {
        if (IsAbsolute)
            return ExecuteAt!.Value.UtcDateTime;

        if (IsOffset)
            return baseTime + Offset!.Value;

        if (IsCron)
        {
            // Ленивое кеширование: парсим один раз, даже если объект пришёл из БД
            if (_parsedCron is null)
            {
                _parsedCron = Cronos.CronExpression.Parse(CronExpression!, CronFormat.IncludeSeconds);
            }

            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(Timezone!);
            return _parsedCron.GetNextOccurrence(baseTime, timeZone, true);
        }

        throw new InvalidOperationException("Schedule has no valid specification.");
    }

    // ===== Ручное сравнение (исключаем _parsedCron) =====
    public bool Equals(Schedule? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return ExecuteAt == other.ExecuteAt
               && Offset == other.Offset
               && string.Equals(CronExpression, other.CronExpression, StringComparison.Ordinal)
               && string.Equals(Timezone, other.Timezone, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        // Хеш-код на основе бизнес-свойств
        return HashCode.Combine(ExecuteAt, Offset, CronExpression, Timezone);
    }

    public override string ToString() =>
        IsAbsolute ? $"At {ExecuteAt:O}" :
        IsOffset ? $"After {Offset}" :
        IsCron ? $"Cron '{CronExpression}' ({Timezone})" : "Unknown";
}
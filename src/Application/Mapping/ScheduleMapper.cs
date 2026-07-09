using System;
using Application.Dto;
using Domain.ValueObjects;

namespace Application.Mapping;

/// <summary>
/// Статический маппер для преобразования ScheduleDto в доменный объект Schedule.
/// </summary>
public static class ScheduleMapper
{
    public static Schedule MapSchedule(ScheduleDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExecuteAt))
            return Schedule.FromAbsolute(DateTimeOffset.Parse(dto.ExecuteAt));
        if (!string.IsNullOrWhiteSpace(dto.Offset))
            return Schedule.FromOffset(ParseOffset(dto.Offset));
        if (!string.IsNullOrWhiteSpace(dto.Cron))
            return Schedule.FromCron(dto.Cron, dto.Timezone ?? "UTC");
        throw new ArgumentException("Одно из полей ExecuteAt, Offset или Cron должно быть заполнено.");
    }

    private static TimeSpan ParseOffset(string offset)
    {
        var unit = offset[^1];
        var value = int.Parse(offset[..^1]);
        return unit switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            'd' => TimeSpan.FromDays(value),
            // Добавили поддержку недель (w) на будущее
            'w' => TimeSpan.FromDays(value * 7),
            _ => throw new ArgumentException($"Неизвестная единица смещения: {unit}")
        };
    }
}
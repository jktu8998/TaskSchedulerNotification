namespace Application.Dto;

/// <summary>
/// Расписание в "сыром" виде. Одно из полей должно быть заполнено.
/// </summary>
public sealed record ScheduleDto
{
    public string? ExecuteAt { get; init; }   // ISO 8601
    public string? Offset { get; init; }      // "5m", "1h", "2d"
    public string? Cron { get; init; }        // cron-выражение
    public string? Timezone { get; init; }    // IANA
}
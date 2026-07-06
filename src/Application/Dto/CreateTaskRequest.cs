using System.Collections.Generic;
using Domain.Enums;

namespace Application.Dto;

/// <summary>
/// DTO для создания нового задания. Принимается от Web-слоя.
/// Содержит примитивные типы и строки, которые Application смапит в доменные Value Objects.
/// </summary>
public sealed record CreateTaskRequest
{
    /// <summary>Тип задания: "OneTime", "Periodic" или "Polling".</summary>
    public string Type { get; init; }
    public ScheduleDto Schedule { get; init; }
    public ExecutionConfigDto Execution { get; init; }
    public ResultDeliveryConfigDto? ResultDelivery { get; init; }
    public PollingConfigDto? PollingConfig { get; init; }
    public RetryPolicyDto? Retry { get; init; }
    public string? SensitiveData { get; init; }
}

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

/// <summary>
/// Конфигурация HTTP-запроса для выполнения.
/// </summary>
public sealed record ExecutionConfigDto
{
    public string Method { get; init; }
    public string Url { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Конфигурация доставки результата.
/// </summary>
public sealed record ResultDeliveryConfigDto
{
    public string Mode { get; init; }   // "ForwardResponse" или "FixedCall"
    public string Url { get; init; }
    public string Method { get; init; }
    public string? Params { get; init; }
}

/// <summary>
/// Конфигурация polling-задания.
/// </summary>
public sealed record PollingConfigDto
{
    public string Field { get; init; }
    public string? Condition { get; init; }
    public string? Value { get; init; }
    public int IntervalSeconds { get; init; } = 60;
    public bool VerboseLogging { get; init; }
}

/// <summary>
/// Политика повторных попыток.
/// </summary>
public sealed record RetryPolicyDto
{
    public int[]? IntervalsSeconds { get; init; }
}
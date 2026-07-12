

namespace Application.Dto;

/// <summary>
/// DTO для ответа API с информацией о задании.
/// Используется при GET-запросах (одиночное задание или список).
/// </summary>
public sealed record TaskResponse
{
    /// <summary>Идентификатор задания (Guid).</summary>
    public Guid Id { get; init; }

    /// <summary>Идентификатор сервиса-отправителя.</summary>
    public string SenderId { get; init; }

    /// <summary>Тип задания: "OneTime", "Periodic", "Polling".</summary>
    public string Type { get; init; }

    /// <summary>Статус задания: "Created", "Scheduled", "Queued", ...</summary>
    public string Status { get; init; }

    /// <summary>Расписание (в формате, аналогичном запросу создания).</summary>
    public ScheduleDto Schedule { get; init; }

    /// <summary>Параметры HTTP-запроса.</summary>
    public ExecutionConfigDto Execution { get; init; }

    /// <summary>Параметры доставки результата (null, если не задано).</summary>
    public ResultDeliveryConfigDto? ResultDelivery { get; init; }

    /// <summary>Параметры polling (null, если не polling-задание).</summary>
    public PollingConfigDto? PollingConfig { get; init; }

    /// <summary>Политика повторных попыток.</summary>
    public RetryPolicyDto Retry { get; init; }

    /// <summary>Количество уже выполненных попыток (0 при создании).</summary>
    public int CurrentAttempt { get; init; }

    /// <summary>Время создания (с явным смещением UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Время последнего изменения (с явным смещением UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Абсолютное время ближайшего выполнения задания (UTC).
    /// Для одноразовых заданий — конкретное время запуска.
    /// Для периодических — время следующего запуска по расписанию.
    /// null, если задание завершено/отменено или время не определено.
    /// </summary>
    public DateTimeOffset? NextExecutionAt { get; init; }
}
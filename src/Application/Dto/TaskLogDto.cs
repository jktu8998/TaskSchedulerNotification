
namespace Application.Dto;

/// <summary>
/// Запись лога задания для ответа API.
/// </summary>
public sealed record TaskLogDto
{
    /// <summary>Порядковый номер записи (из БД).</summary>
    public long Id { get; init; }

    /// <summary>Идентификатор задания.</summary>
    public Guid TaskId { get; init; }

    /// <summary>Время события.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Тип события: "Created", "Scheduled", "Executing", "Completed", "Failed" и т.д.</summary>
    public string EventType { get; init; }

    /// <summary>Краткое сообщение (опционально).</summary>
    public string? Message { get; init; }

    /// <summary>Детали события в формате JSON (опционально).</summary>
    public string? Details { get; init; }
}
using System;

namespace Application.Dto;

/// <summary>
/// Запись Dead Letter Queue для ответа API.
/// </summary>
public sealed record DlqItemDto
{
    /// <summary>Идентификатор записи в DLQ.</summary>
    public long Id { get; init; }

    /// <summary>Идентификатор оригинального задания.</summary>
    public Guid TaskId { get; init; }
    
    /// <summary>
    /// Id сервиса который кидает задачу 
    /// </summary>
    public string SenderId { get; init; }

    /// <summary>Полный снимок оригинального задания в JSON (Schedule, Execution и т.д.).</summary>
    public string OriginalTaskSnapshot { get; init; }

    /// <summary>Описание ошибки, из-за которой задание попало в DLQ.</summary>
    public string? ErrorDetails { get; init; }

    /// <summary>Время помещения в DLQ.</summary>
    public DateTimeOffset MovedAt { get; init; }
}
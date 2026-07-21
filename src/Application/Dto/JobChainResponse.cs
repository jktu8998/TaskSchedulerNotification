namespace Application.Dto;

/// <summary>
/// Полная информация о цепочке для детального просмотра.
/// </summary>
public sealed record JobChainResponse
{
    public Guid Id { get; init; }
    public string SenderId { get; init; }
    public string Name { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; }
    public int? CurrentStepIndex { get; init; }
    public Guid? CurrentTaskId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Список шагов с их статусами (статус определяется позже).</summary>
    public List<ChainStepStatusDto> Steps { get; init; } = new();
}

/// <summary>
/// Статус одного шага цепочки (для ответа).
/// </summary>
public sealed record ChainStepStatusDto
{
    public int StepIndex { get; init; }
    public ChainStepDto Definition { get; init; }
    public string Status { get; init; } // Pending, Executing, Completed, Failed, Dead
    public Guid? TaskId { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
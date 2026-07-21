namespace Application.Dto;

/// <summary>
/// Запрос на создание цепочки заданий.
/// </summary>
public sealed record CreateJobChainRequest
{
    /// <summary>Имя цепочки (обязательное).</summary>
    public string Name { get; init; }

    /// <summary>Описание (опционально).</summary>
    public string? Description { get; init; }

    /// <summary>Последовательность шагов (минимум один).</summary>
    public List<ChainStepDto> Steps { get; init; } = new();
}
namespace Application.Dto;

/// <summary>
/// Краткая информация о цепочке для списков.
/// </summary>
public sealed record JobChainListItemDto
{
    public Guid Id { get; init; }
    public string SenderId { get; init; }
    public string Name { get; init; }
    public string Status { get; init; }
    public int StepCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
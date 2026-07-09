namespace Application.Dto;

/// <summary>
/// Политика повторных попыток.
/// </summary>
public sealed record RetryPolicyDto
{
    public int[]? IntervalsSeconds { get; init; }
}
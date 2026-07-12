
using Application.Interfaces;

namespace Infrastructure.SystemUtilities;

/// <summary>
/// Реализация IDateTimeProvider, возвращающая реальное UTC-время.
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
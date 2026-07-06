using System;

namespace Application.Interfaces;

/// <summary>
/// Абстракция над системным временем.
/// Позволяет подменять реальное время в тестах.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
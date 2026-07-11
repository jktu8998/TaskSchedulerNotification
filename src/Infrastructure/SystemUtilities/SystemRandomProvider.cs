// Infrastructure/System/SystemRandomProvider.cs

using System;
using Application.Interfaces;

namespace Infrastructure.SystemUtilities;

/// <summary>
/// Реализация IRandomProvider на основе потокобезопасного Random.Shared.
/// </summary>
public sealed class SystemRandomProvider : IRandomProvider
{
    public int Next(int minValue, int maxValue)
    {
        return Random.Shared.Next(minValue, maxValue);
    }
}
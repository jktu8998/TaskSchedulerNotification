namespace Domain.ValueObjects;

/// <summary>
/// Базовая стратегия выполнения задания.
/// Определяет общий таймаут и дискриминатор типа стратегии.
/// Наследники реализуют конкретные протоколы (HTTP, gRPC и т.д.).
/// Сравнение учитывает тип и таймаут, наследники расширяют своими полями.
/// </summary>
public abstract record ExecutionStrategy
{
    /// <summary>
    /// Тип стратегии (например, "http", "grpc").
    /// Обязателен для переопределения в наследниках.
    /// </summary>
    public abstract string StrategyType { get; }

    /// <summary>
    /// Таймаут в секундах. Если null — используется значение по умолчанию (обычно 30).
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Создаёт стратегию с заданным таймаутом (или null для дефолта).
    /// </summary>
    protected ExecutionStrategy(int? timeoutSeconds)
    {
        if (timeoutSeconds.HasValue && timeoutSeconds.Value <= 0)
            throw new ArgumentException("Timeout must be greater than 0.", nameof(timeoutSeconds));
        TimeoutSeconds = timeoutSeconds;
    }

    // Конструктор без параметров для Dapper/десериализации
    protected ExecutionStrategy() { }

    /// <summary>
    /// Базовое сравнение по типу и таймауту. Наследники добавляют свои поля.
    /// </summary>
    public virtual bool Equals(ExecutionStrategy? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(StrategyType, other.StrategyType, StringComparison.Ordinal)
               && TimeoutSeconds == other.TimeoutSeconds;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StrategyType, StringComparer.Ordinal);
        hash.Add(TimeoutSeconds);
        return hash.ToHashCode();
    }
}
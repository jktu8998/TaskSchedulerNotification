using System;

namespace Domain.ValueObjects;

/// <summary>
/// Идентификатор задания. Обёртка над Guid.
/// Добавляет семантику: это не просто Guid, а конкретно идентификатор задания.
/// Сравнивается по значению Guid.
/// </summary>
public readonly record struct TaskId(Guid Value)
{
    /// <summary>
    /// Создаёт новый уникальный идентификатор.
    /// </summary>
    public static TaskId New() => new(Guid.NewGuid());

    /// <summary>
    /// Создаёт идентификатор из существующего Guid (например, при чтении из БД).
    /// </summary>
    public static TaskId From(Guid guid) => new(guid);

    public override string ToString() => Value.ToString();
}

namespace Domain.ValueObjects;

/// <summary>
/// Идентификатор задания. Обёртка над Guid.
/// Добавляет семантику: это не просто Guid, а конкретно идентификатор задания.
/// Сравнивается по значению Guid.
/// </summary>
public readonly record struct TaskId 
{
    public Guid Value { get; }

    /// <summary>
    /// Создаёт TaskId с валидацией на пустой Guid.
    /// </summary>
    /// <param name="value">Guid, не равный Empty.</param>
    /// <exception cref="ArgumentException">Если передан Guid.Empty.</exception>
    public TaskId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("TaskId cannot be empty Guid.", nameof(value));
        Value = value;
    }
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
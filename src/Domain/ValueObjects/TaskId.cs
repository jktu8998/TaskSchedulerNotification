namespace Domain.ValueObjects;

public readonly record struct TaskId(Guid Value)
{
    public static TaskId New() => new(Guid.NewGuid());
    public static TaskId From(Guid guid) => new(guid);
    public override string ToString() => Value.ToString();

}
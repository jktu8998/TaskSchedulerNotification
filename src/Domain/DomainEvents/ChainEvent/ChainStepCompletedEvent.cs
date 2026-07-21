using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

/// <summary>Шаг цепочки успешно выполнен, переход к следующему.</summary>
public sealed record ChainStepCompletedEvent(TaskId ChainId, int StepIndex) : IDomainEvent
{
    public TaskId TaskId => ChainId;
    public bool IsIntermediate => true; // промежуточное, так как цепочка ещё не завершена
}
using Domain.ValueObjects;

namespace Domain.DomainEvents.ChainEvent;

public sealed record ChainStepFailedEvent(TaskId ChainId, int StepIndex, string? ErrorDetails) : IDomainEvent
{
    public TaskId TaskId => ChainId;
    public bool IsIntermediate => true;
}
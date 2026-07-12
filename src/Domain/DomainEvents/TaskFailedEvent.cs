using Domain.ValueObjects;

namespace Domain.DomainEvents;

/// <summary>Выполнение завершилось ошибкой, но остались повторные попытки.</summary>
public sealed record TaskFailedEvent(TaskId TaskId) : IDomainEvent;

using System.Collections.Immutable;
using Domain.DomainEvents;
using Domain.DomainEvents.ChainEvent;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Агрегат "Цепочка заданий". Управляет последовательным выполнением шагов (ChainStep).
/// Каждый шаг реализуется как отдельное ScheduledTask, создаваемое оркестратором.
/// </summary>
public sealed class JobChain : IHasDomainEvents
{
    // ========== Свойства ==========
    public TaskId Id { get; private set; }            // Уникальный идентификатор цепочки
    public SenderId SenderId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public ChainStatus Status { get; private set; }

    /// <summary>Упорядоченный список шагов (0-based).</summary>
    public ImmutableArray<ChainStep> Steps { get; private set; }

    /// <summary>Индекс текущего выполняемого шага. -1, если цепочка ещё не начиналась или завершена.</summary>
    public int CurrentStepIndex { get; private set; }

    /// <summary>Идентификатор задания, созданного для текущего шага (если выполняется).</summary>
    public TaskId? CurrentTaskId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Доменные события
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // Пустой конструктор для маппинга (Dapper)
    private JobChain() { }

    /// <summary>
    /// Создаёт новую цепочку в статусе Created.
    /// </summary>
    /// <param name="id">Идентификатор цепочки.</param>
    /// <param name="senderId">Идентификатор сервиса-отправителя.</param>
    /// <param name="name">Человекочитаемое имя цепочки.</param>
    /// <param name="steps">Последовательность шагов (не менее 1).</param>
    /// <param name="description">Описание (опционально).</param>
    /// <param name="utcNow">Текущее UTC-время (для временных меток).</param>
    public JobChain(
        TaskId id,
        SenderId senderId,
        string name,
        IReadOnlyList<ChainStep> steps,
        string? description,
        DateTime utcNow)
    {
        // Валидация
        if (senderId == default)
            throw new ArgumentNullException(nameof(senderId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Chain name cannot be empty.", nameof(name));
        if (steps == null || steps.Count == 0)
            throw new ArgumentException("Chain must contain at least one step.", nameof(steps));

        // Проверка последовательности индексов
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].StepIndex != i)
                throw new ArgumentException($"Step indices must be sequential from 0. Expected {i}, got {steps[i].StepIndex}.", nameof(steps));
        }

        Id = id;
        SenderId = senderId;
        Name = name;
        Description = description;
        Steps = steps.ToImmutableArray();
        Status = ChainStatus.Created;
        CurrentStepIndex = -1;
        CurrentTaskId = null;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;

        // Событие создания (цепочка пока не активна, ждёт запуска)
        _domainEvents.Add(new ChainStartedEvent(Id));
    }

    // ========== Методы управления жизненным циклом ==========

    /// <summary>
    /// Запускает цепочку: переводит в Active и инициирует первый шаг.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="firstTaskId">Идентификатор задания, созданного для первого шага (передаётся извне после сохранения задания).</param>
    public void Start(DateTime utcNow, TaskId firstTaskId)
    {
        if (Status != ChainStatus.Created)
            throw new InvalidOperationException($"Cannot start chain in status {Status}.");
        if (firstTaskId == default)
            throw new ArgumentNullException(nameof(firstTaskId));

        Status = ChainStatus.Active;
        CurrentStepIndex = 0;
        CurrentTaskId = firstTaskId;
        UpdatedAt = utcNow;
        // Событие ChainStartedEvent уже было добавлено при создании, повторно не добавляем.
    }

    /// <summary>
    /// Переводит цепочку к следующему шагу после успешного выполнения текущего.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="nextTaskId">Идентификатор задания для следующего шага (создаётся оркестратором).</param>
    /// <exception cref="InvalidOperationException">Если цепочка не в статусе Active.</exception>
    public void AdvanceToNextStep(DateTime utcNow, TaskId nextTaskId)
    {
        if (Status != ChainStatus.Active)
            throw new InvalidOperationException($"Cannot advance chain in status {Status}.");
        if (nextTaskId == default)
            throw new ArgumentNullException(nameof(nextTaskId));

        int completedStepIndex = CurrentStepIndex;
        CurrentStepIndex++;
        CurrentTaskId = nextTaskId;
        UpdatedAt = utcNow;

        _domainEvents.Add(new ChainStepCompletedEvent(Id, completedStepIndex));

        // Если это был последний шаг – цепочка завершена
        if (CurrentStepIndex >= Steps.Length)
        {
            Status = ChainStatus.Completed;
            CurrentTaskId = null;
            _domainEvents.Add(new ChainCompletedEvent(Id));
        }
    }

    /// <summary>
    /// Обрабатывает провал текущего шага (после исчерпания попыток).
    /// Выполняет действие, указанное в OnFailureAction текущего шага.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="errorDetails">Описание ошибки.</param>
    /// <param name="compensateTaskId">Идентификатор задания для компенсирующего шага (если действие Compensate).</param>
    public void FailCurrentStep(DateTime utcNow, string? errorDetails, TaskId? compensateTaskId = null)
    {
        if (Status != ChainStatus.Active)
            throw new InvalidOperationException($"Cannot fail step in chain status {Status}.");
        if (CurrentStepIndex < 0 || CurrentStepIndex >= Steps.Length)
            throw new InvalidOperationException("No active step to fail.");

        var failedStep = Steps[CurrentStepIndex];
        _domainEvents.Add(new ChainStepFailedEvent(Id, CurrentStepIndex, errorDetails));

        switch (failedStep.OnFailureAction)
        {
            case FailureAction.Stop:
                Status = ChainStatus.Failed;
                CurrentTaskId = null;
                _domainEvents.Add(new ChainFailedEvent(Id, errorDetails ?? "Step failed and chain stopped."));
                break;

            case FailureAction.SkipToNext:
                // Переходим к следующему шагу, но для этого нужно, чтобы оркестратор создал задание для следующего шага.
                // Здесь мы только меняем индекс и генерируем событие, которое должно быть обработано оркестратором.
                // Если следующего шага нет – цепочка завершается с ошибкой.
                if (CurrentStepIndex + 1 >= Steps.Length)
                {
                    Status = ChainStatus.Failed;
                    CurrentTaskId = null;
                    _domainEvents.Add(new ChainFailedEvent(Id, "Last step failed and no more steps to skip to."));
                }
                else
                {
                    CurrentStepIndex++;
                    // CurrentTaskId остаётся пока null; оркестратор после обработки события должен создать задание для нового CurrentStepIndex.
                    // Можно выбросить событие, сигнализирующее о необходимости создания задания, или ожидать что оркестратор сам определит.
                    // Для простоты мы просто не меняем TaskId, а в хендлере события ChainStepFailedEvent проверим и создадим задание, если нужно.
                }
                UpdatedAt = utcNow;
                break;

            case FailureAction.Compensate:
                if (failedStep.CompensateStepIndex is null || compensateTaskId is null)
                    throw new InvalidOperationException("Compensate step index and task id must be provided.");
                CurrentStepIndex = failedStep.CompensateStepIndex.Value;
                CurrentTaskId = compensateTaskId;
                UpdatedAt = utcNow;
                // Генерируем событие о переходе? Можно отдельное событие, но пока используем ChainStepFailedEvent, а оркестратор поймёт по CurrentStepIndex.
                break;

            default:
                throw new InvalidOperationException($"Unsupported failure action: {failedStep.OnFailureAction}");
        }
    }

    /// <summary>Приостанавливает цепочку (только из Active).</summary>
    public void Pause(DateTime utcNow)
    {
        if (Status != ChainStatus.Active)
            throw new InvalidOperationException($"Cannot pause chain in status {Status}.");
        Status = ChainStatus.Paused;
        UpdatedAt = utcNow;
        _domainEvents.Add(new ChainPausedEvent(Id));
    }

    /// <summary>Возобновляет приостановленную цепочку.</summary>
    public void Resume(DateTime utcNow)
    {
        if (Status != ChainStatus.Paused)
            throw new InvalidOperationException($"Cannot resume chain in status {Status}.");
        Status = ChainStatus.Active;
        UpdatedAt = utcNow;
        _domainEvents.Add(new ChainResumedEvent(Id));
    }

    /// <summary>Отменяет цепочку (из любого незавершённого статуса).</summary>
    public void Cancel(DateTime utcNow)
    {
        if (Status == ChainStatus.Completed || Status == ChainStatus.Failed || Status == ChainStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel chain in final status {Status}.");
        Status = ChainStatus.Cancelled;
        CurrentTaskId = null;
        UpdatedAt = utcNow;
        _domainEvents.Add(new ChainCancelledEvent(Id));
    }

    /// <summary>
    /// Устанавливает текущий TaskId после создания задания для шага (используется оркестратором).
    /// </summary>
    public void SetCurrentTaskId(TaskId taskId)
    {
        if (Status != ChainStatus.Active && Status != ChainStatus.Created)
            throw new InvalidOperationException("Can only set task id when chain is active or about to start.");
        CurrentTaskId = taskId;
    }

    // ========== IHasDomainEvents ==========
    public void ClearDomainEvents() => _domainEvents.Clear();
}
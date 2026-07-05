 
using Domain.Enums;
using Domain.ValueObjects;
using Domain.DomainEvents;
using TaskStatus = Domain.Enums.TaskStatus;

namespace Domain.Entities;

/// <summary>
/// Агрегат "Задание". Управляет жизненным циклом задания от создания до завершения.
/// Все методы изменения состояния принимают текущее время параметром,
/// что делает сущность тестируемой и не зависящей от системных часов.
/// </summary>
public sealed class ScheduledTask
{
    // ========== Свойства ==========
    public TaskId Id { get; private set; }
    public string SenderId { get; private set; }
    public TaskType Type { get; private set; }
    public TaskStatus Status { get; private set; }
    public Schedule Schedule { get; private set; }
    public ExecutionConfig Execution { get; private set; }
    public ResultDeliveryConfig? ResultDelivery { get; private set; }
    public PollingConfig? PollingConfig { get; private set; }
    public RetryPolicy RetryPolicy { get; private set; }
    public string? EncryptedSensitiveData { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// Время, до которого задача заблокирована воркером (статус Executing).
    /// Если воркер упал, по истечении этого времени другой воркер может перехватить задачу.
    /// null, если задача не в статусе Executing.
    /// </summary>
    public DateTime? LockedUntil { get; private set; }

    // События домена
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // Пустой конструктор для маппинга из БД (Dapper)
    private ScheduledTask() { }

    /// <summary>
    /// Создаёт новое задание в статусе Created.
    /// </summary>
    /// <param name="id">Идентификатор задания.</param>
    /// <param name="senderId">Идентификатор сервиса-отправителя.</param>
    /// <param name="type">Тип задания (OneTime, Periodic, Polling).</param>
    /// <param name="schedule">Расписание выполнения.</param>
    /// <param name="execution">Параметры HTTP-запроса.</param>
    /// <param name="resultDelivery">Настройки доставки результата (опционально).</param>
    /// <param name="pollingConfig">Настройки polling (опционально).</param>
    /// <param name="retryPolicy">Политика повторных попыток (по умолчанию 5 раз через минуту).</param>
    /// <param name="encryptedSensitiveData">Зашифрованные чувствительные данные.</param>
    /// <param name="utcNow">Текущее время (передаётся извне для тестируемости).</param>
    public ScheduledTask(
        TaskId id,
        string senderId,
        TaskType type,
        Schedule schedule,
        ExecutionConfig execution,
        ResultDeliveryConfig? resultDelivery,
        PollingConfig? pollingConfig,
        RetryPolicy? retryPolicy,
        string? encryptedSensitiveData,
        DateTime utcNow)
    {
        Id = id;
        SenderId = senderId;
        Type = type;
        Status = TaskStatus.Created;
        Schedule = schedule;
        Execution = execution;
        ResultDelivery = resultDelivery;
        PollingConfig = pollingConfig;
        RetryPolicy = retryPolicy ?? RetryPolicy.Default;
        EncryptedSensitiveData = encryptedSensitiveData;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
        LockedUntil = null;

        _domainEvents.Add(new TaskCreatedEvent(this));
    }

    // ========== Методы переходов статусов ==========

    /// <summary>
    /// Переводит задание из Created в Scheduled.
    /// </summary>
    public void ScheduleTask(DateTime utcNow)
    {
        if (Status != TaskStatus.Created)
            throw new InvalidOperationException($"Cannot schedule task in status {Status}");
        Status = TaskStatus.Scheduled;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskScheduledEvent(Id));
    }

    /// <summary>
    /// Переводит задание из Scheduled в Queued.
    /// </summary>
    public void Enqueue(DateTime utcNow)
    {
        if (Status != TaskStatus.Scheduled)
            throw new InvalidOperationException($"Cannot enqueue task in status {Status}");
        Status = TaskStatus.Queued;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskQueuedEvent(Id));
    }

    /// <summary>
    /// Начинает выполнение задания. Задание блокируется на время lockDuration,
    /// чтобы другие воркеры не попытались выполнить его параллельно.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="lockDuration">На какое время заблокировать задачу (обычно таймаут выполнения).</param>
    public void StartExecution(DateTime utcNow, TimeSpan lockDuration)
    {
        if (Status != TaskStatus.Queued)
            throw new InvalidOperationException($"Cannot start execution in status {Status}");
        Status = TaskStatus.Executing;
        UpdatedAt = utcNow;
        LockedUntil = utcNow + lockDuration;
        _domainEvents.Add(new TaskExecutionStartedEvent(Id));
    }

    /// <summary>
    /// Завершает задание успешно. Снимает блокировку.
    /// </summary>
    public void CompleteSuccessfully(DateTime utcNow)
    {
        if (Status != TaskStatus.Executing)
            throw new InvalidOperationException($"Cannot complete task in status {Status}");
        Status = TaskStatus.Completed;
        UpdatedAt = utcNow;
        LockedUntil = null; // больше не заблокировано
        _domainEvents.Add(new TaskCompletedEvent(Id));
    }

    /// <summary>
    /// Помечает задание как проваленное (но ещё есть попытки) или перемещает в Dead Letter Queue.
    /// Снимает блокировку.
    /// </summary>
    /// <param name="remainingRetries">Оставшееся количество повторных попыток.</param>
    /// <param name="utcNow">Текущее время.</param>
    public void MarkFailed(int remainingRetries, DateTime utcNow)
    {
        if (Status != TaskStatus.Executing)
            throw new InvalidOperationException($"Cannot fail task in status {Status}");
        
        if (remainingRetries <= 0)
        {
            Status = TaskStatus.Dead;
            _domainEvents.Add(new TaskMovedToDlqEvent(Id));
        }
        else
        {
            Status = TaskStatus.Failed;
            _domainEvents.Add(new TaskFailedEvent(Id));
        }
        UpdatedAt = utcNow;
        LockedUntil = null; // разблокируем, чтобы мог быть подобран планировщиком
    }

    /// <summary>
    /// Приостанавливает периодическое задание (только из Scheduled).
    /// </summary>
    public void Pause(DateTime utcNow)
    {
        if (Status != TaskStatus.Scheduled)
            throw new InvalidOperationException($"Cannot pause task in status {Status}");
        Status = TaskStatus.Paused;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskPausedEvent(Id));
    }

    /// <summary>
    /// Возобновляет приостановленное задание.
    /// </summary>
    public void Resume(DateTime utcNow)
    {
        if (Status != TaskStatus.Paused)
            throw new InvalidOperationException($"Cannot resume task in status {Status}");
        Status = TaskStatus.Scheduled;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskResumedEvent(Id));
    }

    /// <summary>
    /// Отменяет задание. Может быть вызвано из любого статуса, кроме финальных.
    /// Снимает блокировку, если задание выполнялось.
    /// </summary>
    public void Cancel(DateTime utcNow)
    {
        if (Status == TaskStatus.Completed || Status == TaskStatus.Dead || Status == TaskStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel task in final status {Status}");
        Status = TaskStatus.Cancelled;
        UpdatedAt = utcNow;
        LockedUntil = null;
        _domainEvents.Add(new TaskCancelledEvent(Id));
    }

    /// <summary>
    /// Очищает накопленные доменные события (вызывается после их диспетчеризации).
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
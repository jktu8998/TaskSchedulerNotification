 
using Domain.Enums;
using Domain.ValueObjects;
using Domain.DomainEvents;

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
    public StatusTask Status { get; private set; }
    public Schedule Schedule { get; private set; }
    public ExecutionConfig Execution { get; private set; }
    public ResultDeliveryConfig? ResultDelivery { get; private set; }
    public PollingConfig? PollingConfig { get; private set; }
    public RetryPolicy RetryPolicy { get; private set; }
    public string? EncryptedSensitiveData { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    /// <summary>
    /// Вычисленное абсолютное время следующего выполнения (UTC).
    /// Используется планировщиком для поиска заданий, готовых к выполнению.
    /// null, если задание не в статусе Scheduled.
    /// </summary>
    public DateTime? NextExecutionAt { get; private set; }

    /// <summary>
    /// Время, до которого задача заблокирована воркером (статус Executing).
    /// Если воркер упал, по истечении этого времени другой воркер может перехватить задачу.
    /// null, если задача не в статусе Executing.
    /// </summary>
    public DateTime? LockedUntil { get; private set; }
    
    /// <summary>
    /// Версия агрегата для оптимистичной блокировки.
    /// Инкрементируется в БД при каждом обновлении.
    /// API-операции проверяют версию при сохранении, чтобы избежать конфликтов с воркерами.
    /// </summary>
    public int Version { get; private set; }

    // События домена
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    public int CurrentAttempt { get; private set; }

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
        // Валидация senderId
        if (string.IsNullOrWhiteSpace(senderId))
            throw new ArgumentException("SenderId cannot be null or empty.", nameof(senderId));

        // Валидация типа задания
        if (!Enum.IsDefined(type))
            throw new ArgumentException($"Invalid TaskType: {type}.", nameof(type));

        // Остальные обязательные параметры
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));
        if (execution is null)
            throw new ArgumentNullException(nameof(execution));
        Id = id;
        SenderId = senderId;
        Type = type;
        Status = StatusTask.Created;
        Schedule = schedule;
        Execution = execution;
        ResultDelivery = resultDelivery;
        PollingConfig = pollingConfig;
        RetryPolicy = retryPolicy ?? RetryPolicy.Default;
        EncryptedSensitiveData = encryptedSensitiveData;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
        LockedUntil = null;
        CurrentAttempt = 0;
        NextExecutionAt = null; // будет установлено при планировании
        Version = 1; // Начальная версия агрегата
        // событие теперь содержит только TaskId
        _domainEvents.Add(new TaskCreatedEvent(Id));
    }

    // ========== Методы переходов статусов ==========

    /// <summary>
    /// Планирует задание: переводит из Created в Scheduled и фиксирует 
    /// абсолютное время следующего выполнения.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="nextExecutionAt">Абсолютное время ближайшего запуска.</param>
    public void ScheduleTask(DateTime utcNow, DateTime nextExecutionAt)
    {
        if (Status != StatusTask.Created)
            throw new InvalidOperationException($"Cannot schedule task in status {Status}");
    
        Status = StatusTask.Scheduled;
        NextExecutionAt = nextExecutionAt;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskScheduledEvent(Id));
    }
    
    /// <summary>
    /// Переводит задание из Scheduled в Queued.
    /// </summary>
    public void Enqueue(DateTime utcNow)
    {
        if (Status != StatusTask.Scheduled)
            throw new InvalidOperationException($"Cannot enqueue task in status {Status}");
        Status = StatusTask.Queued;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskQueuedEvent(Id));
    }
    /// <summary>
    /// Проверяет, не протухло ли задание (зависло в статусе Executing).
    /// Возвращает причину протухания или NotStale.
    /// </summary>
    /// <param name="utcNow">Текущее время в UTC.</param>
    public StaleReason IsStale(DateTime utcNow)
    {
        if (Status == StatusTask.Executing && LockedUntil <= utcNow)
            return StaleReason.LockExpired;

        return StaleReason.NotStale;
    }

    /// <summary>
    /// Начинает выполнение задания. Задание блокируется на время lockDuration,
    /// чтобы другие воркеры не попытались выполнить его параллельно.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="lockDuration">На какое время заблокировать задачу (обычно таймаут выполнения).</param>
    public void StartExecution(DateTime utcNow, TimeSpan lockDuration)
    {
        if (Status != StatusTask.Queued)
            throw new InvalidOperationException($"Cannot start execution in status {Status}");
        Status = StatusTask.Executing;
        UpdatedAt = utcNow;
        LockedUntil = utcNow + lockDuration;
        _domainEvents.Add(new TaskExecutionStartedEvent(Id));
    }

    /// <summary>
    /// Завершает задание успешно. Снимает блокировку.
    /// </summary>
    public void CompleteSuccessfully(DateTime utcNow)
    {
        if (Status != StatusTask.Executing)
            throw new InvalidOperationException($"Cannot complete task in status {Status}");
        Status = StatusTask.Completed;
        UpdatedAt = utcNow;
        LockedUntil = null; // больше не заблокировано
        _domainEvents.Add(new TaskCompletedEvent(Id));
    }

    /// <summary>
    /// Помечает задание как проваленное после неудачной попытки выполнения.
    /// Сущность САМА считает попытки и решает, уйти в Dead или остаться в Failed.
    /// Вызывающая сторона просто сообщает: "задание упало".
    /// </summary>
    /// <param name="utcNow">Время падения.</param>
    /// <param name="errorDetails">Детали ошибки (опционально, для логирования).</param>
    public void MarkFailed(DateTime utcNow, string? errorDetails = null)
    {
        if (Status != StatusTask.Executing)
            throw new InvalidOperationException($"Cannot fail task in status {Status}");

        CurrentAttempt++; // Инкремент попыток внутри агрегата

        if (CurrentAttempt >= RetryPolicy.MaxAttempts)
        {
            Status = StatusTask.Dead;
            _domainEvents.Add(new TaskMovedToDlqEvent(Id));// IsIntermediate = false
        }
        else
        {
            Status = StatusTask.Failed;
            _domainEvents.Add(new TaskFailedEvent(Id, isIntermediate: true));// промежуточное событие
        }

        UpdatedAt = utcNow;
        LockedUntil = null;
    }

    /// <summary>
    /// Приостанавливает периодическое задание (только из Scheduled).
    /// </summary>
    public void Pause(DateTime utcNow)
    {
        if (Status != StatusTask.Scheduled)
            throw new InvalidOperationException($"Cannot pause task in status {Status}");
        Status = StatusTask.Paused;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskPausedEvent(Id));
    }

    /// <summary>
    /// Возобновляет приостановленное задание.
    /// </summary>
    public void Resume(DateTime utcNow)
    {
        if (Status != StatusTask.Paused)
            throw new InvalidOperationException($"Cannot resume task in status {Status}");
        Status = StatusTask.Scheduled;
        UpdatedAt = utcNow;
        _domainEvents.Add(new TaskResumedEvent(Id));
    }

    /// <summary>
    /// Отменяет задание. Может быть вызвано из любого статуса, кроме финальных.
    /// Снимает блокировку, если задание выполнялось.
    /// </summary>
    public void Cancel(DateTime utcNow)
    {
        if (Status == StatusTask.Completed || Status == StatusTask.Dead || Status == StatusTask.Cancelled)
            throw new InvalidOperationException($"Cannot cancel task in final status {Status}");
        Status = StatusTask.Cancelled;
        UpdatedAt = utcNow;
        LockedUntil = null;
        _domainEvents.Add(new TaskCancelledEvent(Id));
    }
    
    /// <summary>
    /// Перепланирует периодическое задание после выполнения.
    /// Переводит из Executing обратно в Scheduled и обновляет время следующего запуска.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="nextExecutionAt">Абсолютное время следующего запуска.</param>
    public void Reschedule(DateTime utcNow, DateTime nextExecutionAt)
    {
        if (Status != StatusTask.Executing)
            throw new InvalidOperationException($"Cannot reschedule task in status {Status}");
    
        Status = StatusTask.Scheduled;
        NextExecutionAt = nextExecutionAt;
        UpdatedAt = utcNow;
        LockedUntil = null;
        // Здесь можно добавить событие TaskRescheduledEvent, но пока KISS — не будем плодить события.
        // Просто переиспользуем TaskScheduledEvent, если нужно логирование.
        _domainEvents.Add(new TaskScheduledEvent(Id));
    }
    
    /// <summary>
    /// Переводит задание из Failed в Scheduled для повторной попытки.
    /// Вызывается после неудачного выполнения, если CurrentAttempt < MaxAttempts.
    /// </summary>
    /// <param name="utcNow">Текущее время.</param>
    /// <param name="nextExecutionAt">Абсолютное время следующей попытки (utcNow + интервал из RetryPolicy).</param>
    public void ScheduleRetry(DateTime utcNow, DateTime nextExecutionAt)
    {
        if (Status != StatusTask.Failed)
            throw new InvalidOperationException($"Cannot schedule retry in status {Status}");

        Status = StatusTask.Scheduled;
        NextExecutionAt = nextExecutionAt;
        UpdatedAt = utcNow;
        LockedUntil = null; // разблокируем задачу, она больше не в Executing

        _domainEvents.Add(new TaskScheduledEvent(Id));
    }

    /// <summary>
    /// Очищает накопленные доменные события (вызывается после их диспетчеризации).
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
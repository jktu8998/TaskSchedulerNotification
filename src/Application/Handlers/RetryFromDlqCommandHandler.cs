using Newtonsoft.Json;
using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды повторной попытки задания из DLQ.
/// Загружает снимок задания из Dead Letter Queue, проверяет принадлежность отправителю,
/// создаёт новое задание с теми же параметрами, но новым Id и сброшенным счётчиком попыток,
/// сохраняет и удаляет запись из DLQ в одной транзакции.
/// </summary>
public sealed class RetryFromDlqCommandHandler : ICommandHandler<RetryFromDlqCommand, Guid>
{
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly IDateTimeProvider _dateTime;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;

    public RetryFromDlqCommandHandler(
        IDeadLetterRepository dlqRepo,
        ITaskRepository taskRepo,
        IDateTimeProvider dateTime,
        IUnitOfWork unitOfWork,
        IDomainEventDispatcher dispatcher,
        IRequestContext requestContext)
    {
        _dlqRepo = dlqRepo;
        _taskRepo = taskRepo;
        _dateTime = dateTime;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
        _requestContext = requestContext;
    }

    public async Task<Guid> HandleAsync(RetryFromDlqCommand command, CancellationToken cancellationToken = default)
    {
        var dlqEntry = await _dlqRepo.GetByIdAsync(command.DlqEntryId, cancellationToken);
        if (dlqEntry == null)
            throw new InvalidOperationException("DLQ entry not found.");

        // Десериализуем снимок задания
        // Десериализуем снимок задания (Newtonsoft.Json корректно работает с private set)
        var originalTask = JsonConvert.DeserializeObject<ScheduledTask>(dlqEntry.OriginalTaskSnapshot);
        if (originalTask == null)
            throw new InvalidOperationException("Failed to deserialize task snapshot.");

        // Проверка изоляции: сервис может перезапускать только свои задания
        if (originalTask.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Task not found or access denied.");

        var utcNow = _dateTime.UtcNow;

        // Создаём новое задание с теми же параметрами, но новым Id.
        // Конструктор автоматически инициализирует CurrentAttempt = 0 и статус Created.
        var newTask = new ScheduledTask(
            TaskId.New(),
            originalTask.SenderId,
            originalTask.Type,
            originalTask.Schedule,
            originalTask.Execution,
            originalTask.ResultDelivery,
            originalTask.PollingConfig,
            originalTask.RetryPolicy,
            originalTask.EncryptedSensitiveData,
            utcNow);

        // Вычисляем время следующего выполнения и планируем задание
        var nextExecutionAt = newTask.Schedule.GetNextOccurrence(newTask.CreatedAt)
            ?? throw new InvalidOperationException("Cron expression has no future occurrences.");
        newTask.ScheduleTask(utcNow, nextExecutionAt);

        // Сохраняем новое задание и удаляем запись DLQ в одной транзакции
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _taskRepo.AddAsync(newTask, cancellationToken);
            await _dispatcher.DispatchAsync(newTask.DomainEvents, cancellationToken);
            await _dlqRepo.RemoveAsync(dlqEntry.Id, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            newTask.ClearDomainEvents();
            throw;
        }
        newTask.ClearDomainEvents();

        return newTask.Id.Value;
    }
}
using Application.Commands;
using Application.Dto;
using Application.Interfaces;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды создания нового задания.
/// Отвечает за маппинг DTO в доменную модель, сохранение и диспетчеризацию событий.
/// </summary>
public sealed class CreateTaskCommandHandler : ICommandHandler<CreateTaskCommand, Guid>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IEncryptionService _encryption;
    private readonly IDateTimeProvider _dateTime;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;

    public CreateTaskCommandHandler(
        ITaskRepository taskRepo,
        IEncryptionService encryption,
        IDateTimeProvider dateTime,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork,
        IDomainEventDispatcher dispatcher)
    {
        _taskRepo = taskRepo;
        _encryption = encryption;
        _dateTime = dateTime;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
    }

    public async Task<Guid> HandleAsync(CreateTaskCommand command, CancellationToken cancellationToken = default)
    {
        var req = command.Request;
        var utcNow = _dateTime.UtcNow;

        // 1. Расписание
        var schedule = ScheduleMapper.MapSchedule(req.Schedule);

        // 2. Стратегия выполнения (полиморфно)
        var strategy = TaskMapper.CreateExecutionStrategy(req.Execution);

        // 3. Опциональные конфигурации
        ResultDeliveryConfig? resultDelivery = null;
        if (req.ResultDelivery != null)
        {
            var mode = Enum.Parse<ResultDeliveryMode>(req.ResultDelivery.Mode, ignoreCase: true);
            resultDelivery = new ResultDeliveryConfig(mode, req.ResultDelivery.Url,
                req.ResultDelivery.Method, req.ResultDelivery.Params);
        }

        PollingConfig? pollingConfig = null;
        if (req.PollingConfig != null)
        {
            pollingConfig = new PollingConfig(req.PollingConfig.Field, req.PollingConfig.Condition,
                req.PollingConfig.Value, req.PollingConfig.IntervalSeconds, req.PollingConfig.VerboseLogging);
        }

        RetryPolicy retryPolicy = req.Retry?.IntervalsSeconds is { Length: > 0 } intervals
            ? new RetryPolicy(intervals)
            : RetryPolicy.Default;

        // 4. Шифрование
        string? encrypted = null;
        if (!string.IsNullOrWhiteSpace(req.SensitiveData))
            encrypted = _encryption.Encrypt(req.SensitiveData);

        // 5. Создание агрегата
        var task = new ScheduledTask(
            TaskId.New(),
            _requestContext.SenderId,
            Enum.Parse<TaskType>(req.Type, ignoreCase: true),
            schedule,
            strategy,
            resultDelivery,
            pollingConfig,
            retryPolicy,
            encrypted,
            utcNow);

        var nextExecutionAt = task.Schedule.GetNextOccurrence(task.CreatedAt)
            ?? throw new InvalidOperationException("Cron expression has no future occurrences.");
        task.ScheduleTask(utcNow, nextExecutionAt);

        // 6. Регистрируем агрегат в UnitOfWork для автоочистки событий при коммите
        _unitOfWork.Track(task);

        // 7. Сохранение и диспетчеризация событий (транзакция управляется декоратором)
        await _taskRepo.AddAsync(task);
        await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);

        return task.Id.Value;
    }
}
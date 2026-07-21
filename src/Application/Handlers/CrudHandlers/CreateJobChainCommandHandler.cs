using System.Text.Json;
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
/// Обработчик команды создания цепочки заданий.
/// Создаёт агрегат JobChain, первое задание для первого шага и запускает цепочку.
/// </summary>
public sealed class CreateJobChainCommandHandler : ICommandHandler<CreateJobChainCommand, Guid>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly IChainTaskFactory _chainTaskFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;
    private readonly IDateTimeProvider _dateTime;

    public CreateJobChainCommandHandler(
        IJobChainRepository chainRepo,
        ITaskRepository taskRepo,
        IChainTaskFactory chainTaskFactory,
        IUnitOfWork unitOfWork,
        IDomainEventDispatcher dispatcher,
        IRequestContext requestContext,
        IDateTimeProvider dateTime)
    {
        _chainRepo = chainRepo;
        _taskRepo = taskRepo;
        _chainTaskFactory = chainTaskFactory;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
        _requestContext = requestContext;
        _dateTime = dateTime;
    }

    public async Task<Guid> HandleAsync(CreateJobChainCommand command, CancellationToken cancellationToken = default)
    {
        var request = command.Request;
        var utcNow = _dateTime.UtcNow;
        var senderId = _requestContext.SenderId;

        // 1. Маппинг шагов из DTO в доменные объекты
        var steps = MapSteps(request.Steps);

        // 2. Создание агрегата цепочки
        var chainId = TaskId.New();
        var chain = new JobChain(chainId, senderId, request.Name, steps, request.Description, utcNow);

        // 3. Создание задания для первого шага через фабрику цепочек
        var firstStep = steps[0];
        var firstTask = _chainTaskFactory.CreateTaskForStep(firstStep, chain.Id, senderId, utcNow, stepIndex: 0);

        // 4. Запуск цепочки
        chain.Start(utcNow, firstTask.Id);

        // 5. Сохранение в БД
        _unitOfWork.Track(chain);
        _unitOfWork.Track(firstTask); // тоже нужно для очистки событий
        await _chainRepo.AddAsync(chain, cancellationToken);
        await _taskRepo.AddAsync(firstTask, cancellationToken);

        // 6. Диспетчеризация событий
        await _dispatcher.DispatchAsync(chain.DomainEvents, cancellationToken);
        await _dispatcher.DispatchAsync(firstTask.DomainEvents, cancellationToken);

        return chainId.Value;
    }

    /// <summary>
    /// Преобразует список ChainStepDto в доменные ChainStep.
    /// </summary>
    private static IReadOnlyList<ChainStep> MapSteps(List<ChainStepDto> dtos)
    {
        return dtos.Select(dto => new ChainStep(
            stepIndex: dto.StepIndex,
            execution: TaskMapper.CreateExecutionStrategy(dto.Execution),
            schedule: dto.Schedule != null ? ScheduleMapper.MapSchedule(dto.Schedule) : null,
            resultDelivery: dto.ResultDelivery != null ? MapResultDelivery(dto.ResultDelivery) : null,
            pollingConfig: dto.PollingConfig != null ? MapPollingConfig(dto.PollingConfig) : null,
            retryPolicy: dto.Retry?.IntervalsSeconds is { Length: > 0 } intervals
                ? new RetryPolicy(intervals)
                : null,
            condition: Enum.Parse<TransitionCondition>(dto.TransitionCondition, ignoreCase: true),
            conditionValue: dto.ConditionValue,
            onFailureAction: Enum.Parse<FailureAction>(dto.FailureAction, ignoreCase: true),
            compensateStepIndex: dto.CompensateStepIndex
        )).ToList().AsReadOnly();
    }

    private static ResultDeliveryConfig MapResultDelivery(ResultDeliveryConfigDto dto)
    {
        var mode = Enum.Parse<ResultDeliveryMode>(dto.Mode, ignoreCase: true);
        return new ResultDeliveryConfig(mode, dto.Url, dto.Method, dto.Params);
    }

    private static PollingConfig MapPollingConfig(PollingConfigDto dto)
    {
        return new PollingConfig(dto.Field, dto.Condition, dto.Value, dto.IntervalSeconds, dto.VerboseLogging);
    }
}
    

   
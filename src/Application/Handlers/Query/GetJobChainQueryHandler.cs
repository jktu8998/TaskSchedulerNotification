using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик запроса на получение информации о цепочке заданий.
/// Загружает цепочку из репозитория и маппит в DTO ответа.
/// Проверяет принадлежность цепочки текущему отправителю.
/// </summary>
public sealed class GetJobChainQueryHandler : IQueryHandler<GetJobChainQuery, JobChainResponse>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly IRequestContext _requestContext;

    public GetJobChainQueryHandler(IJobChainRepository chainRepo, IRequestContext requestContext)
    {
        _chainRepo = chainRepo;
        _requestContext = requestContext;
    }

    public async Task<JobChainResponse> HandleAsync(GetJobChainQuery query, CancellationToken cancellationToken = default)
    {
        var chainId = TaskId.From(query.ChainId);
        var chain = await _chainRepo.GetByIdAsync(chainId, cancellationToken);

        if (chain == null || chain.SenderId != _requestContext.SenderId)
            return null; // или выбросить исключение, но для запросов лучше возвращать null

        return MapToResponse(chain);
    }

    /// <summary>
    /// Маппит агрегат JobChain в JobChainResponse.
    /// Статусы шагов пока не заполняются детально (можно расширить позже).
    /// </summary>
    private static JobChainResponse MapToResponse(Domain.Entities.JobChain chain)
    {
        var steps = chain.Steps.Select(step => new ChainStepStatusDto
        {
            StepIndex = step.StepIndex,
            Definition = MapStepToDto(step),
            Status = "Pending", // будет уточнено при интеграции с заданиями
            TaskId = null,       // можно заполнить, если хранить маппинг step -> taskId
            CompletedAt = null
        }).ToList();

        return new JobChainResponse
        {
            Id = chain.Id.Value,
            SenderId = chain.SenderId.ToString(),
            Name = chain.Name,
            Description = chain.Description,
            Status = chain.Status.ToString(),
            CurrentStepIndex = chain.CurrentStepIndex,
            CurrentTaskId = chain.CurrentTaskId?.Value,
            CreatedAt = new DateTimeOffset(chain.CreatedAt, TimeSpan.Zero),
            UpdatedAt = chain.UpdatedAt.HasValue
                ? new DateTimeOffset(chain.UpdatedAt.Value, TimeSpan.Zero)
                : null,
            Steps = steps
        };
    }

    /// <summary>
    /// Преобразует доменный ChainStep в DTO ChainStepDto.
    /// </summary>
    private static ChainStepDto MapStepToDto(Domain.ValueObjects.ChainStep step)
    {
        return new ChainStepDto
        {
            StepIndex = step.StepIndex,
            Schedule = step.Schedule != null ? MapScheduleToDto(step.Schedule) : null,
            Execution = step.Execution != null ? MapExecutionToDto(step.Execution) : null,
            ResultDelivery = step.ResultDelivery != null ? MapResultDeliveryToDto(step.ResultDelivery) : null,
            PollingConfig = step.PollingConfig != null ? MapPollingConfigToDto(step.PollingConfig) : null,
            Retry = step.RetryPolicy != null
                ? new RetryPolicyDto { IntervalsSeconds = step.RetryPolicy.IntervalsSeconds.ToArray() }
                : null,
            TransitionCondition = step.Condition.ToString(),
            ConditionValue = step.ConditionValue,
            FailureAction = step.OnFailureAction.ToString(),
            CompensateStepIndex = step.CompensateStepIndex
        };
    }

    private static ScheduleDto MapScheduleToDto(Schedule schedule) => new()
    {
        ExecuteAt = schedule.ExecuteAt?.ToString("o"),
        Offset = schedule.Offset.HasValue ? $"{(int)schedule.Offset.Value.TotalSeconds}s" : null,
        Cron = schedule.CronExpression,
        Timezone = schedule.Timezone
    };

    private static ExecutionConfigDto MapExecutionToDto(ExecutionStrategy strategy)
    {
        if (strategy is HttpExecutionConfig http)
        {
            return new ExecutionConfigDto
            {
                ExecutionType = "http",
                Method = http.Method,
                Url = http.Url,
                Headers = http.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Body = http.Body,
                TimeoutSeconds = http.TimeoutSeconds
            };
        }
        return new ExecutionConfigDto { ExecutionType = strategy.StrategyType };
    }

    private static ResultDeliveryConfigDto MapResultDeliveryToDto(ResultDeliveryConfig config) =>
        new()
        {
            Mode = config.Mode.ToString(),
            Url = config.Url,
            Method = config.Method,
            Params = config.Params
        };

    private static PollingConfigDto MapPollingConfigToDto(PollingConfig config) =>
        new()
        {
            Field = config.Field,
            Condition = config.Condition,
            Value = config.Value,
            IntervalSeconds = config.IntervalSeconds,
            VerboseLogging = config.VerboseLogging
        };
}
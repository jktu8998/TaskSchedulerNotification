using System.Text.Json;
using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

public sealed class RunPollingCheckCommandHandler : ICommandHandler<RunPollingCheckCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IPollingStateRepository _stateRepo;
    private readonly IHttpExecutor _httpExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IOutboxRepository _outboxRepo;

    private const int BatchSize = 100;

    public RunPollingCheckCommandHandler(
        ITaskRepository taskRepo,
        IPollingStateRepository stateRepo,
        IHttpExecutor httpExecutor,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTime,
        IDomainEventDispatcher dispatcher,
        IOutboxRepository outboxRepo)
    {
        _taskRepo = taskRepo;
        _stateRepo = stateRepo;
        _httpExecutor = httpExecutor;
        _unitOfWork = unitOfWork;
        _dateTime = dateTime;
        _dispatcher = dispatcher;
        _outboxRepo = outboxRepo;
    }
    //TODO:проблема N+1 нужно будет решить её тут 
    //отдельно потом этот метод доделай 
    public async Task HandleAsync(RunPollingCheckCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;
        // Шаг 1: Выборка заданий в отдельной транзакции (нужна для SKIP LOCKED)
        List<ScheduledTask> tasks;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            tasks = (await _taskRepo.GetScheduledPollingTasksAsync(utcNow, BatchSize, cancellationToken)).ToList();
            await _unitOfWork.CommitAsync(cancellationToken); // фиксируем и освобождаем блокировки
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
        // Шаг 2: Обработка каждого задания в собственной транзакции
        foreach (var task in tasks)
        {
            // Атомарно захватываем задачу
            // 2.1. Атомарный захват задачи в короткой транзакции
            ScheduledTask? acquiredTask;
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                acquiredTask = await _taskRepo.TryAcquirePollingTaskAsync(
                    task.Id, utcNow, TimeSpan.FromSeconds(30), cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }

            if (acquiredTask is null) continue;

            // Выполняем HTTP-запрос
            HttpResponseResult response;
            try
            {
                var http = (HttpExecutionConfig)acquiredTask.Execution;
                var requestConfig = new HttpRequestConfig(http.Method, http.Url, http.Headers, http.Body)
                {
                    Timeout = TimeSpan.FromSeconds(http.TimeoutSeconds ?? 30)
                };
                response = await _httpExecutor.ExecuteAsync(requestConfig, cancellationToken);
            }
            catch (Exception ex)
            {
                // Ошибка выполнения запроса — перепланируем без изменений
                // Обработка ошибки, перепланирование без изменения состояния
                // Для этого нужна транзакция, так как обновляем задание
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                try
                {
                    var interval = TimeSpan.FromSeconds(acquiredTask.PollingConfig!.IntervalSeconds);
                    acquiredTask.RescheduleAfterPolling(utcNow, interval);
                    _unitOfWork.Track(acquiredTask);
                    await _taskRepo.UpdateAsync(acquiredTask, acquiredTask.Version, cancellationToken);
                    await _dispatcher.DispatchAsync(acquiredTask.DomainEvents, cancellationToken);
                    await _unitOfWork.CommitAsync(cancellationToken);
                }
                catch
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    throw;
                }
                continue;
            }

            // Загружаем предыдущее состояние
            var previousState = await _stateRepo.GetByTaskIdAsync(acquiredTask.Id, cancellationToken);
            bool hasChanged = false;
            string? newResponseJson = response.IsSuccess ? response.Body : null;

            if (!response.IsSuccess)
            {
                hasChanged = false;
            }
            else if (previousState?.LastResponseJson is null)
            {
                hasChanged = true; // первая проверка
            }
            else
            {
                hasChanged = HasChanged(previousState.LastResponseJson, newResponseJson!, acquiredTask.PollingConfig!);
            }

            // Обновляем состояние
            var state = previousState ?? new PollingState(acquiredTask.Id);
            state.UpdateState(newResponseJson, utcNow);

            // ----------  доставка через Outbox ----------
            OutboxMessage? deliveryMessage = null;
            if (hasChanged && acquiredTask.ResultDelivery is not null)
            {
                var deliveryPayload = JsonSerializer.Serialize(new
                {
                    mode = acquiredTask.ResultDelivery.Mode.ToString(),
                    url = acquiredTask.ResultDelivery.Url,
                    method = acquiredTask.ResultDelivery.Method,
                    body = acquiredTask.ResultDelivery.Mode == ResultDeliveryMode.ForwardResponse
                        ? response.Body
                        : acquiredTask.ResultDelivery.Params
                });

                deliveryMessage = new OutboxMessage(
                    acquiredTask.Id,
                    "ResultDeliveryRequested",
                    deliveryPayload,
                    utcNow,
                    maxRetries: 3);
            }

            // Перепланируем задание
            var pollingInterval = TimeSpan.FromSeconds(acquiredTask.PollingConfig!.IntervalSeconds);
            acquiredTask.RescheduleAfterPolling(utcNow, pollingInterval);

            // Сохраняем всё в одной транзакции
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                _unitOfWork.Track(acquiredTask);
                await _taskRepo.UpdateAsync(acquiredTask, acquiredTask.Version, cancellationToken);
                await _stateRepo.UpsertAsync(state, cancellationToken);
                if (deliveryMessage is not null)
                    await _outboxRepo.AddAsync(deliveryMessage, cancellationToken);
                await _dispatcher.DispatchAsync(acquiredTask.DomainEvents, cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    private bool HasChanged(string previousJson, string newJson, PollingConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Condition))
            return !string.Equals(previousJson, newJson, StringComparison.Ordinal);

        using var prevDoc = JsonDocument.Parse(previousJson);
        using var newDoc = JsonDocument.Parse(newJson);

        if (!newDoc.RootElement.TryGetProperty(config.Field, out var newValue)) return false;
        if (!prevDoc.RootElement.TryGetProperty(config.Field, out var prevValue)) return true;

        return config.Condition switch
        {
            "changed" or "not_equal" => !prevValue.GetRawText().Equals(newValue.GetRawText(), StringComparison.Ordinal),
            "greater_than" => Compare(prevValue, newValue) < 0,
            _ => false
        };
    }

    private int Compare(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
            return a.GetDecimal().CompareTo(b.GetDecimal());
        return string.Compare(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
    }
}
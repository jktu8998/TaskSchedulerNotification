using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.DomainEventHandlers;

/// <summary>
/// Обработчик события попадания задания в Dead Letter Queue.
/// Если задание является частью цепочки, выполняет действие, указанное в FailureAction шага.
/// </summary>
public sealed class ChainStepFailureHandler : IDomainEventHandler<TaskMovedToDlqEvent>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly IChainTaskFactory _chainTaskFactory;
    private readonly ITaskRepository _taskRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;

    public ChainStepFailureHandler(
        IJobChainRepository chainRepo,
        IChainTaskFactory chainTaskFactory,
        ITaskRepository taskRepo,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTime)
    {
        _chainRepo = chainRepo;
        _chainTaskFactory = chainTaskFactory;
        _taskRepo = taskRepo;
        _unitOfWork = unitOfWork;
        _dateTime = dateTime;
    }

    public async Task HandleAsync(TaskMovedToDlqEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var failedTask = await _taskRepo.GetByIdAsync(domainEvent.TaskId, cancellationToken);
        if (failedTask?.ChainId == null)
            return; // задание не принадлежит цепочке

        var chain = await _chainRepo.GetByIdAsync(failedTask.ChainId.Value, cancellationToken);
        if (chain == null || chain.Status != ChainStatus.Active)
            return; // цепочка уже не активна

        // Проверяем, что это действительно текущий шаг
        if (chain.CurrentTaskId != failedTask.Id)
            return;

        var currentStep = chain.Steps[chain.CurrentStepIndex];
        var utcNow = _dateTime.UtcNow;

        // Определяем, нужно ли создать новое задание (для SkipToNext и Compensate)
        ScheduledTask? nextTask = null;
        TaskId? compensateTaskId = null;

        // Подготавливаем задание в зависимости от действия
        switch (currentStep.OnFailureAction)
        {
            case FailureAction.SkipToNext:
                int nextIndex = chain.CurrentStepIndex + 1;
                if (nextIndex < chain.Steps.Length)
                {
                    var nextStep = chain.Steps[nextIndex];
                    nextTask = _chainTaskFactory.CreateTaskForStep(
                        nextStep, chain.Id, chain.SenderId.ToString(), utcNow, nextIndex);
                }
                break;

            case FailureAction.Compensate:
                if (currentStep.CompensateStepIndex.HasValue)
                {
                    int compensateIndex = currentStep.CompensateStepIndex.Value;
                    if (compensateIndex >= 0 && compensateIndex < chain.Steps.Length)
                    {
                        var compensateStep = chain.Steps[compensateIndex];
                        nextTask = _chainTaskFactory.CreateTaskForStep(
                            compensateStep, chain.Id, chain.SenderId.ToString(), utcNow, compensateIndex);
                        compensateTaskId = nextTask?.Id;
                    }
                }
                break;

            case FailureAction.Stop:
                // ничего не делаем, просто остановим цепочку
                break;
        }

        // Вызываем доменный метод, который обрабатывает провал и меняет состояние цепочки
        chain.FailCurrentStep(utcNow, domainEvent.TaskId.ToString() + " moved to DLQ", compensateTaskId);

        // Сохраняем цепочку и, если есть, новое задание
        _unitOfWork.Track(chain);
        await _chainRepo.UpdateAsync(chain, chain.Version, cancellationToken);

        if (nextTask != null)
        {
            _unitOfWork.Track(nextTask);
            await _taskRepo.AddAsync(nextTask, cancellationToken);
        }
    }
}
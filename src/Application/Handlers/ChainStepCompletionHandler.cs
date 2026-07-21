using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;


namespace Application.Handlers;

/// <summary>
/// Обработчик события успешного выполнения задания.
/// Если задание является частью цепочки, проверяет условия перехода и продвигает цепочку.
/// </summary>
public sealed class ChainStepCompletionHandler : IDomainEventHandler<TaskCompletedEvent>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly IChainTaskFactory _chainTaskFactory;
    private readonly ITaskRepository _taskRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;

    public ChainStepCompletionHandler(
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

    public async Task HandleAsync(TaskCompletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var completedTask = await _taskRepo.GetByIdAsync(domainEvent.TaskId, cancellationToken);
        if (completedTask?.ChainId == null) return; // задание не принадлежит цепочке

        var chain = await _chainRepo.GetByIdAsync(completedTask.ChainId.Value, cancellationToken);
        if (chain == null || chain.Status != ChainStatus.Active)
            return; // цепочка уже не активна (могли отменить/приостановить параллельно)

        // Проверяем, что это действительно текущий шаг цепочки
        if (chain.CurrentTaskId != completedTask.Id)
        {
            // Задание завершилось, но цепочка уже ушла вперёд (возможное дублирование, игнорируем)
            return;
        }

        var currentStep = chain.Steps[chain.CurrentStepIndex];

        // Проверяем условие перехода. Для успешного выполнения допустимы Always и OnSuccess.
        // В будущем можно расширить анализом ответа.
        bool shouldAdvance = currentStep.Condition switch
        {
            TransitionCondition.Always => true,
            TransitionCondition.OnSuccess => true, // задача успешно выполнена
            TransitionCondition.OnFailure => false, // не должно происходить по Completed, но на всякий случай
            TransitionCondition.IfStatusCode => false, // TODO: нужен доступ к статусу ответа
            TransitionCondition.IfBodyContains => false, // TODO: нужен доступ к телу ответа
            _ => false
        };

        if (!shouldAdvance)
        {
            // Если условие не позволяет перейти, останавливаем цепочку (можно добавить кастомное действие)
            chain.Cancel(_dateTime.UtcNow);
            await _chainRepo.UpdateAsync(chain, chain.Version, cancellationToken);
            return;
        }

        // Создаём задание для следующего шага
        var nextStepIndex = chain.CurrentStepIndex + 1;
        ScheduledTask nextTask = null;
        if (nextStepIndex < chain.Steps.Length)
        {
            var nextStep = chain.Steps[nextStepIndex];
            nextTask = _chainTaskFactory.CreateTaskForStep(
                nextStep,
                chain.Id,
                chain.SenderId.ToString(),
                _dateTime.UtcNow,
                nextStepIndex);
        }

        // Продвигаем цепочку
        chain.AdvanceToNextStep(_dateTime.UtcNow, nextTask?.Id ?? default);

        // Сохраняем изменения
        _unitOfWork.Track(chain);
        await _chainRepo.UpdateAsync(chain, chain.Version, cancellationToken);

        if (nextTask != null)
        {
            _unitOfWork.Track(nextTask);
            await _taskRepo.AddAsync(nextTask, cancellationToken);
        }
    }
}
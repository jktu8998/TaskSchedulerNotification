using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;


namespace Application.Handlers;

public sealed class RunChainHeartbeatCommandHandler : ICommandHandler<RunChainHeartbeatCommand>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly IChainTaskFactory _chainTaskFactory;
    private readonly ITaskRepository _taskRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;

    public RunChainHeartbeatCommandHandler(
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

    public async Task HandleAsync(RunChainHeartbeatCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;
        var staleChains = await _chainRepo.GetStaleActiveChainsAsync(utcNow, cancellationToken);

        if (staleChains.Count == 0)
            return;

        foreach (var chain in staleChains)
        {
            // Проверяем, что задание всё ещё зависло (повторная проверка внутри транзакции)
            var taskId = chain.CurrentTaskId!.Value;
            var task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);
            if (task == null || task.Status != StatusTask.Executing)
                continue; // уже неактуально

            var currentStep = chain.Steps[chain.CurrentStepIndex];
            ScheduledTask? nextTask = null;

            // Подготавливаем задание для SkipToNext / Compensate
            switch (currentStep.OnFailureAction)
            {
                case FailureAction.SkipToNext:
                    if (chain.CurrentStepIndex + 1 < chain.Steps.Length)
                    {
                        var nextStep = chain.Steps[chain.CurrentStepIndex + 1];
                        nextTask = _chainTaskFactory.CreateTaskForStep(nextStep, chain.Id, chain.SenderId.ToString(), utcNow, chain.CurrentStepIndex + 1);
                    }
                    break;

                case FailureAction.Compensate:
                    if (currentStep.CompensateStepIndex.HasValue)
                    {
                        int idx = currentStep.CompensateStepIndex.Value;
                        if (idx >= 0 && idx < chain.Steps.Length)
                        {
                            var compStep = chain.Steps[idx];
                            nextTask = _chainTaskFactory.CreateTaskForStep(compStep, chain.Id, chain.SenderId.ToString(), utcNow, idx);
                        }
                    }
                    break;
            }

            // Выполняем доменную логику провала шага
            chain.FailCurrentStep(utcNow, "Execution timed out (recovered by Chain Heartbeat)", nextTask?.Id);

            // Сохраняем цепочку и новое задание (всё в рамках текущей транзакции декоратора)
            _unitOfWork.Track(chain);
            await _chainRepo.UpdateAsync(chain, chain.Version, cancellationToken);

            if (nextTask != null)
            {
                _unitOfWork.Track(nextTask);
                await _taskRepo.AddAsync(nextTask, cancellationToken);
            }
        }
    }
}
using System.Text.Json;
using Application.Commands;
using Application.Interfaces;
using Domain.DomainEvents;
using Domain.Entities;
using Domain.Interfaces;

namespace Application.Handlers;

/// <summary>
/// Обработчик планирования заданий.
/// Загружает все задачи, готовые к выполнению, переводит их в Queued
/// и атомарно сохраняет вместе с Outbox-сообщениями.
/// </summary>
public sealed class RunSchedulingCommandHandler : ICommandHandler<RunSchedulingCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IOutboxRepository _outboxRepo;
    private readonly IDateTimeProvider _dateTime;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;
    private const int BatchSize = 200;

    public RunSchedulingCommandHandler(
        ITaskRepository taskRepo,
        IOutboxRepository outboxRepo,
        IDateTimeProvider dateTime,
        IUnitOfWork unitOfWork,
        IDomainEventDispatcher dispatcher)
    {
        _taskRepo = taskRepo;
        _outboxRepo = outboxRepo;
        _dateTime = dateTime;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
    }
    
    public async Task HandleAsync(RunSchedulingCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;

        // 1. Начинаем транзакцию перед любым обращением к БД
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var readyTasks = await _taskRepo.GetScheduledBeforeAsync(utcNow, BatchSize, cancellationToken);

            if (readyTasks.Count == 0)
            {
                await _unitOfWork.CommitAsync(cancellationToken); // фиксируем пустую транзакцию
                return;
            }

            var tasksToUpdate = new List<ScheduledTask>(readyTasks.Count);
            var outboxMessages = new List<OutboxMessage>(readyTasks.Count);

            foreach (var task in readyTasks)
            {
                task.Enqueue(utcNow);
                var queuedEvent = new TaskQueuedEvent(task.Id);
                var outboxMessage = new OutboxMessage(
                    task.Id,
                    nameof(TaskQueuedEvent),
                    JsonSerializer.Serialize(queuedEvent),
                    utcNow);
                tasksToUpdate.Add(task);
                outboxMessages.Add(outboxMessage);
                _unitOfWork.Track(task);
            }

            await _taskRepo.BulkUpdateAsync(tasksToUpdate, cancellationToken);
            await _outboxRepo.BulkAddAsync(outboxMessages, cancellationToken);

            var allEvents = tasksToUpdate.SelectMany(t => t.DomainEvents).ToList();
            await _dispatcher.DispatchAsync(allEvents, cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
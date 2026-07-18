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
        
            var readyTasks = await _taskRepo.GetScheduledBeforeAsync(utcNow, BatchSize, cancellationToken);

            if (readyTasks.Count == 0) return;
                // return;

            var tasksToUpdate = new List<ScheduledTask>(readyTasks.Count);
            var outboxMessages = new List<OutboxMessage>(readyTasks.Count);

            // Подготавливаем все изменения в памяти
            foreach (var task in readyTasks)
            {
                task.Enqueue(utcNow);

                // Создаём outbox-сообщение для события TaskQueuedEvent
                var queuedEvent = new TaskQueuedEvent(task.Id);
                var outboxMessage = new OutboxMessage(
                    task.Id,
                    nameof(TaskQueuedEvent), // "TaskQueuedEvent"
                    JsonSerializer.Serialize(queuedEvent),
                    utcNow);

                tasksToUpdate.Add(task);
                outboxMessages.Add(outboxMessage);

                // Регистрируем агрегат для автоматической очистки событий при коммите
                _unitOfWork.Track(task);
            }

            // Атомарно сохраняем всё в одной транзакции
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await _taskRepo.BulkUpdateAsync(tasksToUpdate, cancellationToken);
                await _outboxRepo.BulkAddAsync(outboxMessages, cancellationToken);

                // Диспетчеризуем все накопленные события
                var allEvents = tasksToUpdate.SelectMany(t => t.DomainEvents).ToList();
                await _dispatcher.DispatchAsync(allEvents, cancellationToken);

                await _unitOfWork.CommitAsync(cancellationToken);
                // CommitAsync вызовет ClearDomainEvents у всех Tracked агрегатов
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }
    }
}
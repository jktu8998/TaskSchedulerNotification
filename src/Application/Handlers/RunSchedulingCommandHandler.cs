using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды RunSchedulingCommand.
/// Находит задания, готовые к выполнению (Scheduled + NextExecutionAt <= utcNow),
/// переводит их в Queued и атомарно (в рамках транзакции) записывает
/// исходящее сообщение в Outbox для гарантированной отправки в очередь.
/// Сбой публикации в брокер больше не влияет на консистентность состояния заданий.
/// </summary>
public sealed class RunSchedulingCommandHandler : ICommandHandler<RunSchedulingCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IOutboxRepository _outboxRepo;     // <-- Новая зависимость
    private readonly IDateTimeProvider _dateTime;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;

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

        // Загружаем все задания, у которых наступило время выполнения
        var readyTasks = await _taskRepo.GetScheduledBeforeAsync(utcNow, cancellationToken);

        foreach (var task in readyTasks)
        {
            // Для каждого задания открываем свою транзакцию
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                // 1. Бизнес-переход: Scheduled -> Queued
                task.Enqueue(utcNow);

                // 2. Сохраняем изменения задания в БД
                await _taskRepo.UpdateAsync(task, cancellationToken);

                // 3. Создаём запись в Outbox (гарантирует, что задание будет отправлено в очередь)
                var outboxMessage = new OutboxMessage(task.Id, utcNow);
                await _outboxRepo.AddAsync(outboxMessage, cancellationToken);

                // 4. Диспетчеризуем события (логирование)
                await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);

                // 5. Фиксируем транзакцию: задание в Queued + сообщение в Outbox атомарно
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Если что-то пошло не так — откатываем только это задание
                await _unitOfWork.RollbackAsync(cancellationToken);
                // TODO: добавить инфраструктурный логгер
                // _logger.LogError(ex, "Failed to schedule task {TaskId}", task.Id);
                task.ClearDomainEvents();
                continue; // переходим к следующему заданию
            }
            finally
            {
                // Гарантированно очищаем события после обработки
                task.ClearDomainEvents();
            }
        }
    }
}

///### Шаг 4: Фоновый воркер (The Relay)

/*Это новый компонент, который будет жить в слое Infrastructure (обычный `BackgroundService` из ASP.NET Core). Его задача — непрерывно перекладывать сообщения из БД в брокер.

- **Логика работы:**
    
1. Просыпается раз в N миллисекунд (например, 500 мс).
        
2. Забирает пачку сообщений через `IOutboxRepository.GetUnprocessedAsync` (с использованием `SELECT FOR UPDATE SKIP LOCKED` для конкурентной работы).
        
3. В цикле пытается отправить каждое задание через `IMessageQueue.PublishScheduledTaskAsync`.
        
4. При успехе — удаляет запись из базы (`RemoveAsync`).
        
5. При ошибке сети RabbitMQ — просто прекращает работу и ждет следующего цикла (база сохранит данные, ничего не потеряется).*/

/// 
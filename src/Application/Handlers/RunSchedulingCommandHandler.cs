using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Interfaces;
using Domain.Interfaces;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды RunSchedulingCommand.
/// Находит задания, готовые к выполнению (Scheduled + NextExecutionAt <= utcNow),
/// переводит их в Queued и отправляет в очередь сообщений.
/// Каждое задание обрабатывается изолированно: сбой одного не влияет на остальные.
/// </summary>
public sealed class RunSchedulingCommandHandler : ICommandHandler<RunSchedulingCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IMessageQueue _messageQueue;
    private readonly IDateTimeProvider _dateTime;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;

    public RunSchedulingCommandHandler(
        ITaskRepository taskRepo,
        IMessageQueue messageQueue,
        IDateTimeProvider dateTime,
        IUnitOfWork unitOfWork,
        IDomainEventDispatcher dispatcher)
    {
        _taskRepo = taskRepo;
        _messageQueue = messageQueue;
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

                // 2. Сохраняем изменения в БД
                await _taskRepo.UpdateAsync(task, cancellationToken);

                // 3. Диспетчеризуем события (логирование)
                await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);

                // 4. Фиксируем транзакцию
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

            // 5. ТОЛЬКО после успешного коммита отправляем сообщение в RabbitMQ
            try
            {
                await _messageQueue.PublishScheduledTaskAsync(task.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                // БД уже закоммичена, задание в Queued. RabbitMQ недоступен.
                // TODO: логировать и рассчитывать на механизм восстановления (heartbeat)
                // _logger.LogError(ex, "Failed to publish task {TaskId} to RabbitMQ", task.Id);
            }
            finally
            {
                // Гарантированно очищаем события после обработки
                task.ClearDomainEvents();
            }
        }
    }
}
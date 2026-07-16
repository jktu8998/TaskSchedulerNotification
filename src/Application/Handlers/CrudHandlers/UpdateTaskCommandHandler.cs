using Application.Commands;
using Application.Interfaces;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers.CrudHandlers;

/// <summary>
/// Обработчик команды изменения задания.
/// Отменяет существующее задание и создаёт новое с обновлёнными полями,
/// после чего сохраняет оба изменения в одной транзакции.
/// Возвращает идентификатор нового задания.
/// </summary>
public sealed class UpdateTaskCommandHandler : ICommandHandler<UpdateTaskCommand, Guid>
{
    private readonly ITaskRepository _taskRepo;
    private readonly ITaskFactory _taskFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;
    private readonly IDateTimeProvider _dateTime;

    public UpdateTaskCommandHandler(
        ITaskRepository taskRepo,
        ITaskFactory taskFactory,
        IUnitOfWork unitOfWork,
        IDomainEventDispatcher dispatcher,
        IRequestContext requestContext,
        IDateTimeProvider dateTime)
    {
        _taskRepo = taskRepo;
        _taskFactory = taskFactory;
        _unitOfWork = unitOfWork;
        _dispatcher = dispatcher;
        _requestContext = requestContext;
        _dateTime = dateTime;
    }

    public async Task<Guid> HandleAsync(UpdateTaskCommand command, CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTime.UtcNow;

        // 1. Загружаем и отменяем старое задание
        var oldTaskId = TaskId.From(command.TaskId);
        var oldTask = await _taskRepo.GetByIdAsync(oldTaskId, cancellationToken);

        if (oldTask == null || oldTask.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Task not found or access denied.");
        var expectedVersion = oldTask.Version;
        oldTask.Cancel(utcNow);

        // 2. Создаём новое задание через фабрику (уже в статусе Scheduled)
        var newTask = _taskFactory.CreateFromRequest(
            command.UpdatedFields,
            _requestContext.SenderId,
            utcNow,
            command.UpdatedFields.IdempotencyKey);

        // 3. Регистрируем оба агрегата для автоочистки событий при коммите
        _unitOfWork.Track(oldTask);
        _unitOfWork.Track(newTask);
        try
        {
            // 4. Сохраняем изменения и диспетчеризуем события (транзакция — декоратор)
            await _taskRepo.UpdateAsync(oldTask, expectedVersion, cancellationToken);
            await _dispatcher.DispatchAsync(oldTask.DomainEvents, cancellationToken);

            await _taskRepo.AddAsync(newTask, cancellationToken);
            await _dispatcher.DispatchAsync(newTask.DomainEvents, cancellationToken);
        }
        catch(ConcurrencyException)
        {
            throw;
        }
       

        return newTask.Id.Value;
    }
}
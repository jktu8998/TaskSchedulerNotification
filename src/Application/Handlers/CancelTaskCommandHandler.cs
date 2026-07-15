using Application.Commands;
using Application.Interfaces;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды отмены задания.
/// Загружает задание, проверяет принадлежность отправителю,
/// вызывает доменный метод Cancel(utcNow) и сохраняет изменения в БД
/// в рамках одной короткой транзакции.
/// Если задание уже в финальном статусе (Completed, Dead, Cancelled), выбрасывает исключение.
/// </summary>
public sealed class CancelTaskCommandHandler : ICommandHandler<CancelTaskCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;

    public CancelTaskCommandHandler(
        ITaskRepository taskRepo,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTime,
        IDomainEventDispatcher dispatcher,
        IRequestContext requestContext)
    {
        _taskRepo = taskRepo;
        _unitOfWork = unitOfWork;
        _dateTime = dateTime;
        _dispatcher = dispatcher;
        _requestContext = requestContext;
    }

    public async Task HandleAsync(CancelTaskCommand command, CancellationToken cancellationToken = default)
    {
        var taskId = TaskId.From(command.TaskId);
        var task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);

        if (task == null || task.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Task not found or access denied.");

        var utcNow = _dateTime.UtcNow;
        task.Cancel(utcNow);

        // Регистрируем агрегат для автоматической очистки событий при коммите
        _unitOfWork.Track(task);

        await _taskRepo.UpdateAsync(task, cancellationToken);
        await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);
    }
}
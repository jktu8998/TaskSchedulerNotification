using Application.Commands;
using Application.Interfaces;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers.CrudHandlers;
/// <summary>
/// Обработчик команды приостановки задания.
/// Загружает задание, проверяет принадлежность отправителю,
/// вызывает доменный метод Pause(utcNow) и сохраняет изменения в БД
/// в рамках одной короткой транзакции.
/// Если статус задания не Scheduled, выбрасывает исключение.
/// </summary>
public sealed class PauseTaskCommandHandler : ICommandHandler<PauseTaskCommand>
{
    private readonly ITaskRepository _taskRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;

    public PauseTaskCommandHandler(
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

    public async Task HandleAsync(PauseTaskCommand command, CancellationToken cancellationToken = default)
    {
        var taskId = TaskId.From(command.TaskId);
        var task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);

        if (task == null || task.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Task not found or access denied.");

        var utcNow = _dateTime.UtcNow;
        task.Pause(utcNow);

        _unitOfWork.Track(task);

        await _taskRepo.UpdateAsync(task, cancellationToken);
        await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);
    }
}
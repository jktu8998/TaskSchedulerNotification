using Application.Commands;
using Application.Interfaces;
using Domain.Interfaces;

namespace Application.Handlers.CrudHandlers;

public sealed class CreateTaskCommandHandler : ICommandHandler<CreateTaskCommand, Guid>
{
    private readonly ITaskRepository _taskRepo;
    private readonly ITaskFactory _taskFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;
    private readonly IDateTimeProvider _dateTime;

    public CreateTaskCommandHandler(
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

    public async Task<Guid> HandleAsync(CreateTaskCommand command, CancellationToken cancellationToken = default)
    {
        // Проверка идемпотентности
        var existingTask = await _taskRepo.GetByIdempotencyKeyAsync(command.Request.IdempotencyKey, cancellationToken);
        if (existingTask is not null)
        {
            // Проверяем принадлежность тому же отправителю (безопасность)
            if (existingTask.SenderId != _requestContext.SenderId)
                throw new InvalidOperationException("A task with this idempotency key exists but belongs to a different sender.");

            return existingTask.Id.Value;
        }
        var task = _taskFactory.CreateFromRequest(
            command.Request,
            _requestContext.SenderId,
            _dateTime.UtcNow,
            command.Request.IdempotencyKey);

        _unitOfWork.Track(task);
        await _taskRepo.AddAsync(task, cancellationToken);
        await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);

        return task.Id.Value;
    }
}
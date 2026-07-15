using System.Text.Json;
using Application.Commands;
using Application.Dto;
using Application.Interfaces;
using Domain.Interfaces;

namespace Application.Handlers;

public sealed class RetryFromDlqCommandHandler : ICommandHandler<RetryFromDlqCommand, Guid>
{
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly ITaskFactory _taskFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskRepository _taskRepo;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;
    private readonly IDateTimeProvider _dateTime;

    public RetryFromDlqCommandHandler(
        IDeadLetterRepository dlqRepo,
        ITaskFactory taskFactory,
        IUnitOfWork unitOfWork,
        ITaskRepository taskRepo,
        IDomainEventDispatcher dispatcher,
        IRequestContext requestContext,
        IDateTimeProvider dateTime)
    {
        _dlqRepo = dlqRepo;
        _taskFactory = taskFactory;
        _unitOfWork = unitOfWork;
        _taskRepo = taskRepo;
        _dispatcher = dispatcher;
        _requestContext = requestContext;
        _dateTime = dateTime;
    }

    public async Task<Guid> HandleAsync(RetryFromDlqCommand command, CancellationToken cancellationToken = default)
    {
        var dlqEntry = await _dlqRepo.GetByIdAsync(command.DlqEntryId, cancellationToken);
        if (dlqEntry == null)
            throw new InvalidOperationException("DLQ entry not found.");

        // Десериализуем снапшот (System.Text.Json)
        var snapshot = JsonSerializer.Deserialize<TaskSnapshotDto>(dlqEntry.OriginalTaskSnapshot);
        if (snapshot == null)
            throw new InvalidOperationException("Failed to deserialize task snapshot.");

        if (snapshot.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Task not found or access denied.");

        // Создаём новый агрегат через фабрику, используя снапшот (sensitive уже зашифрован)
        var newTask = _taskFactory.CreateFromSnapshot(snapshot, _requestContext.SenderId, _dateTime.UtcNow);

        _unitOfWork.Track(newTask);
        await _taskRepo.AddAsync(newTask, cancellationToken);  
        await _dispatcher.DispatchAsync(newTask.DomainEvents, cancellationToken);

        // Удаляем запись DLQ (можно внутри той же транзакции)
        await _dlqRepo.RemoveAsync(dlqEntry.Id, cancellationToken);

        return newTask.Id.Value;
    }
}
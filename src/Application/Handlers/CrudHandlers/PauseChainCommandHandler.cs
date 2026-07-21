using Application.Commands;
using Application.Interfaces;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды приостановки цепочки заданий.
/// Загружает цепочку, проверяет принадлежность отправителю,
/// вызывает доменный метод Pause(utcNow) и сохраняет изменения.
/// Если цепочка не в статусе Active, выбрасывает исключение.
/// </summary>
public sealed class PauseChainCommandHandler : ICommandHandler<PauseChainCommand>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;

    public PauseChainCommandHandler(
        IJobChainRepository chainRepo,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTime,
        IDomainEventDispatcher dispatcher,
        IRequestContext requestContext)
    {
        _chainRepo = chainRepo;
        _unitOfWork = unitOfWork;
        _dateTime = dateTime;
        _dispatcher = dispatcher;
        _requestContext = requestContext;
    }

    public async Task HandleAsync(PauseChainCommand command, CancellationToken cancellationToken = default)
    {
        var chainId = TaskId.From(command.ChainId);
        var chain = await _chainRepo.GetByIdAsync(chainId, cancellationToken);

        if (chain == null || chain.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Chain not found or access denied.");

        var utcNow = _dateTime.UtcNow;
        chain.Pause(utcNow);

        _unitOfWork.Track(chain);

        try
        {
            await _chainRepo.UpdateAsync(chain, expectedVersion: chain.Version, cancellationToken);
            await _dispatcher.DispatchAsync(chain.DomainEvents, cancellationToken);
        }
        catch (ConcurrencyException)
        {
            throw;
        }
    }
}
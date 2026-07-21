using Application.Commands;
using Application.Interfaces;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Application.Handlers;

/// <summary>
/// Обработчик команды возобновления приостановленной цепочки.
/// </summary>
public sealed class ResumeChainCommandHandler : ICommandHandler<ResumeChainCommand>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTime;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IRequestContext _requestContext;

    public ResumeChainCommandHandler(
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

    public async Task HandleAsync(ResumeChainCommand command, CancellationToken cancellationToken = default)
    {
        var chainId = TaskId.From(command.ChainId);
        var chain = await _chainRepo.GetByIdAsync(chainId, cancellationToken);

        if (chain == null || chain.SenderId != _requestContext.SenderId)
            throw new InvalidOperationException("Chain not found or access denied.");

        var utcNow = _dateTime.UtcNow;
        chain.Resume(utcNow);

        _unitOfWork.Track(chain);

        try
        {
            await _chainRepo.UpdateAsync(chain, chain.Version, cancellationToken);
            await _dispatcher.DispatchAsync(chain.DomainEvents, cancellationToken);
        }
        catch (ConcurrencyException)
        {
            throw;
        }
    }
}
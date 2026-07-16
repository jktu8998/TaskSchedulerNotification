using Application.Interfaces;

namespace Application.Decorator;

/// <summary>
/// Декоратор, обеспечивающий транзакционность для команд с ITransactionalCommand.
/// Для остальных команд просто делегирует вызов.
/// </summary>
public sealed class TransactionCommandHandlerDecorator<TCommand> : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    private readonly ICommandHandler<TCommand> _inner;
    private readonly IUnitOfWork _unitOfWork;

    public TransactionCommandHandlerDecorator(ICommandHandler<TCommand> inner, IUnitOfWork unitOfWork)
    {
        _inner = inner;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        if (command is ITransactionalCommand)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await _inner.HandleAsync(command, cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
                // Очистка доменных событий после коммита — теперь ответственность декоратора
                // Но хендлер должен предоставить доступ к агрегату для очистки.
                // Пока оставим так, в следующем уточнении доработаем.
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }
        }
        else
        {
            await _inner.HandleAsync(command, cancellationToken);
        }
    }
}
//для команд с результатом
public sealed class TransactionCommandHandlerDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly IUnitOfWork _unitOfWork;

    public TransactionCommandHandlerDecorator(ICommandHandler<TCommand, TResult> inner, IUnitOfWork unitOfWork)
    {
        _inner = inner;
        _unitOfWork = unitOfWork;
    }

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        if (command is ITransactionalCommand)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await _inner.HandleAsync(command, cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }
        }
        else
        {
            return await _inner.HandleAsync(command, cancellationToken);
        }
    }
}
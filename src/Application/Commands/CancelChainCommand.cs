using Application.Interfaces;

namespace Application.Commands;

/// <summary>Команда отмены цепочки (переводит в Cancelled).</summary>
public sealed record CancelChainCommand(Guid ChainId) : ICommand, ITransactionalCommand;
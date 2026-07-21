using Application.Interfaces;

namespace Application.Commands;

/// <summary>Команда приостановки активной цепочки.</summary>
public sealed record PauseChainCommand(Guid ChainId) : ICommand, ITransactionalCommand;
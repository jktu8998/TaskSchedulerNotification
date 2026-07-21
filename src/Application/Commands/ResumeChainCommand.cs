using Application.Interfaces;

namespace Application.Commands;

/// <summary>
/// Команда на возобновление приостановленной цепочки заданий.
/// </summary>
public sealed record ResumeChainCommand(Guid ChainId) : ICommand, ITransactionalCommand;
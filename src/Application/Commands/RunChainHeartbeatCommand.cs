using Application.Interfaces;

namespace Application.Commands;

/// <summary>
/// Команда для запуска механизма восстановления зависших цепочек.
/// Вызывается фоновым воркером ChainHeartbeatWorker.
/// </summary>
public sealed record RunChainHeartbeatCommand : ICommand, ITransactionalCommand;
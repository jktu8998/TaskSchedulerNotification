
namespace Application.Commands;

/// <summary>
/// Команда выполнения задания. Вызывается фоновым исполнителем (ExecutorBackgroundService)
/// для каждого TaskId, полученного из очереди сообщений.
/// </summary>
public sealed record RunExecutionCommand(Guid TaskId) : ICommand;
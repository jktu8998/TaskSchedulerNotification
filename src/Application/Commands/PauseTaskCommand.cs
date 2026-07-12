
namespace Application.Commands;
/// <summary>
/// Команда на приостановку задания.
/// Переводит задание из Scheduled в Paused.
/// Задание в других статусах не может быть приостановлено.
/// </summary>
public sealed record PauseTaskCommand(Guid TaskId) : ICommand;
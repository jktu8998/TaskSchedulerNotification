using System;
namespace Application.Commands;

/// <summary>
/// Команда на возобновление приостановленного задания.
/// Переводит задание из Paused обратно в Scheduled.
/// </summary>
public sealed record ResumeTaskCommand(Guid TaskId) : ICommand;
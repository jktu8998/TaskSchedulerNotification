using System;

namespace Application.Commands;

/// <summary>
/// Команда на отмену задания.
/// </summary>
public sealed record CancelTaskCommand(Guid TaskId) : ICommand;
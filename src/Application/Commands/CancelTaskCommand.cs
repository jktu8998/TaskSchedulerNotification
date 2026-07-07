using System;

namespace Application.Commands;

/// <summary>
/// Команда отмены задания. Переводит задание в статус Cancelled.
/// Может быть вызвана из любого статуса, кроме финальных (Completed, Dead, Cancelled).
/// </summary>
public sealed record CancelTaskCommand(Guid TaskId) : ICommand;
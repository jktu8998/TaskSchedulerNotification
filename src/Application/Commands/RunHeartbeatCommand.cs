

namespace Application.Commands;

/// <summary>
/// Команда для запуска механизма восстановления зависших заданий.
/// Вызывается фоновым воркером (например, раз в минуту).
/// </summary>
public sealed record RunHeartbeatCommand : ICommand;
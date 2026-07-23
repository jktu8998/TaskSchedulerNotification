using Application.Interfaces;

namespace Application.Commands;

/// <summary>
/// Команда для запуска цикла планирования. Вызывается фоновым воркером.
/// </summary>
public sealed record RunSchedulingCommand : ICommand, IManagesOwnTransaction;
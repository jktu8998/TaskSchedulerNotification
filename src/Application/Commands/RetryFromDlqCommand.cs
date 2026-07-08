using System;
namespace Application.Commands;

/// <summary>
/// Команда на повторную попытку задания из Dead Letter Queue.
/// Принимает идентификатор записи в DLQ, пересоздаёт задание и возвращает 
/// идентификатор нового созданного задания.
/// </summary>
public sealed record RetryFromDlqCommand(long DlqEntryId) : ICommand<Guid>; // возвращает новый TaskId  
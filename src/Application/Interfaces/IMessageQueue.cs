using System;
using System.Threading.Tasks;
using Domain.ValueObjects;

namespace Application.Interfaces;

/// <summary>
/// Абстракция над очередью сообщений для передачи заданий исполнителю.
/// </summary>
public interface IMessageQueue
{
    /// <summary>
    /// Опубликовать задание в очередь.
    /// </summary>
    Task PublishScheduledTaskAsync(TaskId taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Подписаться на очередь. Вызывает handler при поступлении нового задания.
    /// </summary>
    Task SubscribeAsync(Func<TaskId, Task> handler, CancellationToken cancellationToken = default);
}
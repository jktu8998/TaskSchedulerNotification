
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
    /// Вроде как  тут этот метод нахуй не нужен  зачем-то он тут появился, но
    /// Потребитель очереди реализован как самостоятельный фоновый воркер TaskExecutionWorker,
    /// который напрямую работает с RabbitMQ через AsyncEventingBasicConsumer 
    /// </summary>
    Task SubscribeAsync(Func<TaskId, CancellationToken, Task> handler, CancellationToken cancellationToken = default);
}
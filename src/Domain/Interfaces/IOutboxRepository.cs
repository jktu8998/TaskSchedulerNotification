
using Domain.Entities;

namespace Domain.Interfaces;

/// <summary>
/// Контракт для хранилища исходящих сообщений (Transactional Outbox).
/// Реализуется в Infrastructure для обеспечения гарантированной доставки заданий в очередь.
/// </summary>
public interface IOutboxRepository
{
    /// <summary>
    /// Добавить новое сообщение в Outbox.
    /// Вызывается внутри транзакции вместе с сохранением задания.
    /// </summary>
    /// <param name="message">Сообщение для отправки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить пачку необработанных сообщений для отправки в очередь.
    /// Использует SKIP LOCKED для конкурентной обработки несколькими воркерами.
    /// Сообщения отдаются в порядке создания (FIFO).
    /// </summary>
    /// <param name="batchSize">Максимальное количество сообщений в пакете.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список сообщений, готовых к отправке.</returns>
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удалить сообщение из Outbox после успешной отправки в очередь.
    /// Обеспечивает exactly-once: сообщение удаляется только после подтверждения брокером.
    /// </summary>
    /// <param name="outboxMessageId">Идентификатор записи в Outbox.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task RemoveAsync(Guid outboxMessageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Пакетная вставка сообщений в Outbox в одной транзакции.
    /// Реализация использует INSERT ... SELECT UNNEST или аналогичный batch-метод.
    /// </summary>
    /// <param name="messages">Коллекция новых сообщений для вставки.</param>
    /// <param name="ct">Токен отмены.</param>
    Task BulkAddAsync(IReadOnlyCollection<OutboxMessage> messages, CancellationToken ct = default);
    
    /// <summary>
    /// Обновить существующее сообщение (например, увеличить счётчик попыток).
    /// Используется после неудачной доставки, если RetryCount < MaxRetries .
    /// </summary>
    Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
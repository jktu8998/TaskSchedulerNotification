using Dapper;
using Domain.Entities;
using Domain.Interfaces;

namespace Infrastructure.Persistence.Repositories;

public sealed class DapperOutboxRepository : IOutboxRepository
{
    private readonly IDbTransactionContext _db;

    public DapperOutboxRepository(IDbTransactionContext db)
    {
        _db = db;
    }

    public async Task AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO outbox_messages (
                id, task_id, event_type, payload,
                created_at, retry_count, max_retries
            ) VALUES (
                @Id, @TaskId, @EventType, @Payload::jsonb,
                @CreatedAt, @RetryCount, @MaxRetries
            )";

        await _db.Connection.ExecuteAsync(sql, new
        {
            message.Id,
            message.TaskId,
            message.EventType,
            message.Payload,
            message.CreatedAt,
            message.RetryCount,
            message.MaxRetries
        }, transaction: _db.Transaction);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, task_id, event_type, payload,
                   created_at, retry_count, max_retries
            FROM outbox_messages
            ORDER BY created_at
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED";

        var result = await _db.Connection.QueryAsync<OutboxMessage>(sql, new
        {
            BatchSize = batchSize
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task RemoveAsync(Guid outboxMessageId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM outbox_messages WHERE id = @Id";

        await _db.Connection.ExecuteAsync(sql, new
        {
            Id = outboxMessageId
        }, transaction: _db.Transaction);
    }

    // НОВЫЙ: обновление счётчика попыток
    public async Task UpdateAsync(OutboxMessage message, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE outbox_messages SET
                retry_count = @RetryCount,
                max_retries = @MaxRetries
            WHERE id = @Id";

        await _db.Connection.ExecuteAsync(sql, new
        {
            message.Id,
            message.RetryCount,
            message.MaxRetries
        }, transaction: _db.Transaction);
    }

    // НОВЫЙ: пакетная вставка
    public async Task BulkAddAsync(IReadOnlyCollection<OutboxMessage> messages, CancellationToken ct = default)
    {
        // В Dapper нет true batch insert, используем цикл в пределах транзакции
        foreach (var message in messages)
        {
            await AddAsync(message, ct);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Domain.Entities;
using Domain.ValueObjects;
using Application.Interfaces;
using Domain.Interfaces; // IOutboxRepository лежит в Application.Interfaces теперь нет я его перенс в Domain.Interfaces
using Infrastructure.Persistence;

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
            INSERT INTO outbox_messages (id, task_id, created_at)
            VALUES (@Id, @TaskId, @CreatedAt)";

        await _db.Connection.ExecuteAsync(sql, new
        {
            message.Id,
            message.TaskId,   // TaskIdTypeHandler преобразует в Guid
            message.CreatedAt
        }, transaction: _db.Transaction);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, task_id, created_at
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
}
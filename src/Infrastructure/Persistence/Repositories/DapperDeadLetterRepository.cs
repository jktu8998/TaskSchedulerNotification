// Infrastructure/Persistence/Repositories/DapperDeadLetterRepository.cs

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.Repositories;

public sealed class DapperDeadLetterRepository : IDeadLetterRepository
{
    private readonly IDbTransactionContext _db;

    public DapperDeadLetterRepository(IDbTransactionContext db)
    {
        _db = db;
    }

    public async Task AddAsync(DeadLetterEntry entry, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO dead_letter_queue (task_id, sender_id, 
                                           original_task_snapshot, 
                                           error_details, moved_at)
            VALUES (@TaskId, @SenderId, 
                    @OriginalTaskSnapshot::jsonb, 
                    @ErrorDetails, @MovedAt)";

        await _db.Connection.ExecuteAsync(sql, new
        {
            entry.TaskId,
            entry.SenderId,
            entry.OriginalTaskSnapshot,
            entry.ErrorDetails,
            entry.MovedAt
        }, transaction: _db.Transaction);
    }

    public async Task<DeadLetterEntry?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, task_id, sender_id, 
                   original_task_snapshot, 
                   error_details, moved_at 
            FROM dead_letter_queue 
            WHERE id = @Id";

        return await _db.Connection.QuerySingleOrDefaultAsync<DeadLetterEntry>(sql, new
        {
            Id = id
        }, transaction: _db.Transaction);
    }

    public async Task<IReadOnlyList<DeadLetterEntry>> GetBySenderIdAsync(string senderId, int skip, int take, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, task_id, sender_id, 
                   original_task_snapshot, 
                   error_details, moved_at
            FROM dead_letter_queue
            WHERE sender_id = @SenderId
            ORDER BY moved_at DESC
            LIMIT @Take OFFSET @Skip";

        var result = await _db.Connection.QueryAsync<DeadLetterEntry>(sql, new
        {
            SenderId = senderId,
            Skip = skip,
            Take = take
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(int skip, int take, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, task_id, sender_id, 
                   original_task_snapshot, 
                   error_details, moved_at
            FROM dead_letter_queue
            ORDER BY moved_at DESC
            LIMIT @Take OFFSET @Skip";

        var result = await _db.Connection.QueryAsync<DeadLetterEntry>(sql, new
        {
            Skip = skip,
            Take = take
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task RemoveAsync(long id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM dead_letter_queue WHERE id = @Id";

        await _db.Connection.ExecuteAsync(sql, new
        {
            Id = id
        }, transaction: _db.Transaction);
    }
}
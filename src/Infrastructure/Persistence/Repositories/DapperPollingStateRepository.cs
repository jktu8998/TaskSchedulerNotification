
using Dapper;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.Repositories;

public sealed class DapperPollingStateRepository : IPollingStateRepository
{
    private readonly IDbTransactionContext _db;

    public DapperPollingStateRepository(IDbTransactionContext db)
    {
        _db = db;
    }

    public async Task<PollingState?> GetByTaskIdAsync(TaskId taskId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT task_id, last_response_json, last_checked_at
            FROM polling_states
            WHERE task_id = @TaskId";

        return await _db.Connection.QuerySingleOrDefaultAsync<PollingState>(sql, new
        {
            TaskId = taskId
        }, transaction: _db.Transaction);
    }

    public async Task UpsertAsync(PollingState state, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO polling_states (task_id, last_response_json, last_checked_at)
            VALUES (@TaskId, @LastResponseJson, @LastCheckedAt)
            ON CONFLICT (task_id) DO UPDATE SET
                last_response_json = EXCLUDED.last_response_json,
                last_checked_at = EXCLUDED.last_checked_at";

        await _db.Connection.ExecuteAsync(sql, new
        {
            state.TaskId,
            state.LastResponseJson,
            state.LastCheckedAt
        }, transaction: _db.Transaction);
    }
}

using Dapper;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.Repositories;

public sealed class DapperTaskLogRepository : ITaskLogRepository
{
    private readonly IDbTransactionContext _db;

    public DapperTaskLogRepository(IDbTransactionContext db)
    {
        _db = db;
    }

    public async Task AddAsync(TaskLog log, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO task_logs (task_id, timestamp, event_type, message, details)
            VALUES (@TaskId, @Timestamp, @EventType, @Message, @Details)";

        await _db.Connection.ExecuteAsync(sql, new
        {
            log.TaskId,
            log.Timestamp,
            log.EventType,
            log.Message,
            log.Details
        }, transaction: _db.Transaction);
    }

    public async Task<IReadOnlyList<TaskLog>> GetByTaskIdAsync(TaskId taskId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, task_id, timestamp, event_type, message, details
            FROM task_logs
            WHERE task_id = @TaskId
            ORDER BY timestamp";

        var result = await _db.Connection.QueryAsync<TaskLog>(sql, new
        {
            TaskId = taskId
        }, transaction: _db.Transaction);

        return result.AsList();
    }
}
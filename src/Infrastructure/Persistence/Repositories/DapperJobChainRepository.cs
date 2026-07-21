using System.Data;
using Dapper;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.Repositories;

public sealed class DapperJobChainRepository : IJobChainRepository
{
    private readonly IDbTransactionContext _db;

    public DapperJobChainRepository(IDbTransactionContext db) => _db = db;

    public async Task AddAsync(JobChain chain, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO job_chains (
                id, sender_id, name, description, status,
                steps, current_step_index, current_task_id,
                created_at, updated_at, version
            ) VALUES (
                @Id, @SenderId, @Name, @Description, @Status,
                @Steps::jsonb, @CurrentStepIndex, @CurrentTaskId,
                @CreatedAt, @UpdatedAt, @Version
            )";

        await _db.Connection.ExecuteAsync(sql, new
        {
            chain.Id,
            chain.SenderId,
            chain.Name,
            chain.Description,
            Status = (int)chain.Status,
            Steps = chain.Steps,   // будет сериализовано TypeHandler'ом в JSONB
            chain.CurrentStepIndex,
            chain.CurrentTaskId,
            chain.CreatedAt,
            chain.UpdatedAt,
            chain.Version
        }, transaction: _db.Transaction);
    }

    public async Task<JobChain?> GetByIdAsync(TaskId id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                id, sender_id, name, description, status,
                steps, current_step_index, current_task_id,
                created_at, updated_at, version
            FROM job_chains
            WHERE id = @Id";

        return await _db.Connection.QuerySingleOrDefaultAsync<JobChain>(sql, new { Id = id }, transaction: _db.Transaction);
    }

    public async Task<IReadOnlyList<JobChain>> GetBySenderIdAsync(
        SenderId senderId, int skip, int take,
        ChainStatus? status = null, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM job_chains WHERE sender_id = @SenderId";

        if (status.HasValue)
            sql += " AND status = @Status";

        sql += " ORDER BY created_at DESC LIMIT @Take OFFSET @Skip";

        var result = await _db.Connection.QueryAsync<JobChain>(sql, new
        {
            SenderId = senderId.Value,
            Status = status.HasValue ? (int)status.Value : (object)DBNull.Value,
            Skip = skip,
            Take = take
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task UpdateAsync(JobChain chain, int expectedVersion, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE job_chains SET
                name = @Name,
                description = @Description,
                status = @Status,
                steps = @Steps::jsonb,
                current_step_index = @CurrentStepIndex,
                current_task_id = @CurrentTaskId,
                updated_at = @UpdatedAt,
                version = version + 1
            WHERE id = @Id AND version = @ExpectedVersion
            RETURNING version";

        var newVersion = await _db.Connection.QuerySingleOrDefaultAsync<int?>(
            sql,
            new
            {
                chain.Id,
                chain.Name,
                chain.Description,
                Status = (int)chain.Status,
                Steps = chain.Steps,
                chain.CurrentStepIndex,
                chain.CurrentTaskId,
                chain.UpdatedAt,
                ExpectedVersion = expectedVersion
            },
            transaction: _db.Transaction);

        if (newVersion == null)
            throw new ConcurrencyException(chain.Id.Value, expectedVersion);
    }

    public async Task<IReadOnlyList<JobChain>> GetStaleActiveChainsAsync(DateTime utcNow, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                jc.id, jc.sender_id, jc.name, jc.description, jc.status,
                jc.steps, jc.current_step_index, jc.current_task_id,
                jc.created_at, jc.updated_at, jc.version
            FROM job_chains jc
            INNER JOIN tasks t ON jc.current_task_id = t.id
            WHERE jc.status = @ActiveStatus
              AND t.status = @ExecutingStatus
              AND t.locked_until <= @UtcNow
            FOR UPDATE OF jc SKIP LOCKED";

        var result = await _db.Connection.QueryAsync<JobChain>(sql, new
        {
            ActiveStatus = (int)ChainStatus.Active,
            ExecutingStatus = (int)StatusTask.Executing,
            UtcNow = utcNow
        }, transaction: _db.Transaction);

        return result.AsList();
    }
}
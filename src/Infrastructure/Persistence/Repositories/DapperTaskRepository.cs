using Dapper;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.Repositories;

public sealed class DapperTaskRepository : ITaskRepository
{
    private readonly IDbTransactionContext _db;

    public DapperTaskRepository(IDbTransactionContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ScheduledTask task, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO tasks (
                id, sender_id, idempotency_key,
                type, status,
                schedule, execution,
                result_delivery, polling_config, retry_policy,
                encrypted_sensitive_data, raw_payload,
                created_at, updated_at,
                next_execution_at, locked_until, scheduled_at,
                current_attempt, version, metadata
            ) VALUES (
                @Id, @SenderId, @IdempotencyKey,
                @Type, @Status,
                @Schedule, @Strategy,
                @ResultDelivery, @PollingConfig, @RetryPolicy,
                @EncryptedSensitiveData, @RawPayload,
                @CreatedAt, @UpdatedAt,
                @NextExecutionAt, @LockedUntil, @ScheduledAt,
                @CurrentAttempt, @Version, @Metadata
            )";

        await _db.Connection.ExecuteAsync(sql, new
        {
            task.Id,
            task.SenderId,
            task.IdempotencyKey,
            task.Type,
            task.Status,
            task.Schedule,
            Strategy = task.Strategy,         // было task.Execution
            ResultDelivery = task.ResultDelivery,
            PollingConfig = task.PollingConfig,
            task.RetryPolicy,
            task.EncryptedSensitiveData,
            task.RawPayload,
            task.CreatedAt,
            task.UpdatedAt,
            task.NextExecutionAt,
            task.LockedUntil,
            task.ScheduledAt,
            task.CurrentAttempt,
            task.Version,
            Metadata = task.Metadata           // будет обработан TypeHandler'ом
        }, transaction: _db.Transaction);
    }

    public async Task<ScheduledTask?> GetByIdAsync(TaskId id, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM tasks WHERE id = @Id";
        return await _db.Connection.QuerySingleOrDefaultAsync<ScheduledTask>(
            sql,
            new { Id = id },
            transaction: _db.Transaction);
    }

    public async Task<ScheduledTask?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM tasks
            WHERE idempotency_key = @IdempotencyKey
            LIMIT 1";

        return await _db.Connection.QuerySingleOrDefaultAsync<ScheduledTask>(
            sql,
            new { IdempotencyKey = idempotencyKey },
            transaction: _db.Transaction);
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetBySenderIdAsync(
        SenderId senderId, int skip, int take,
        StatusTask? status = null, TaskType? type = null,
        CancellationToken ct = default)
    {
        var sql = "SELECT * FROM tasks WHERE sender_id = @SenderId";

        if (status.HasValue)
            sql += " AND status = @Status";
        if (type.HasValue)
            sql += " AND type = @Type";

        sql += " ORDER BY created_at DESC LIMIT @Take OFFSET @Skip";

        var result = await _db.Connection.QueryAsync<ScheduledTask>(sql, new
        {
            SenderId = senderId.Value,   // используем строку
            Status = status,
            Type = type,
            Skip = skip,
            Take = take
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetScheduledBeforeAsync(
        DateTime cutoff, int batchSize, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM tasks
            WHERE status = @Status AND next_execution_at <= @Cutoff
            ORDER BY next_execution_at ASC
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED";

        var result = await _db.Connection.QueryAsync<ScheduledTask>(sql, new
        {
            Status = StatusTask.Scheduled,
            Cutoff = cutoff,
            BatchSize = batchSize
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetScheduledPollingTasksAsync(
        DateTime utcNow, int batchSize, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM tasks
            WHERE type = @Type AND status = @Status AND next_execution_at <= @UtcNow
            ORDER BY next_execution_at ASC
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED";

        var result = await _db.Connection.QueryAsync<ScheduledTask>(sql, new
        {
            Type = TaskType.Polling,
            Status = StatusTask.Scheduled,
            UtcNow = utcNow,
            BatchSize = batchSize
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task UpdateAsync(ScheduledTask task, int expectedVersion, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE tasks SET
                sender_id = @SenderId,
                type = @Type,
                status = @Status,
                schedule = @Schedule,
                execution = @Strategy,
                result_delivery = @ResultDelivery,
                polling_config = @PollingConfig,
                retry_policy = @RetryPolicy,
                encrypted_sensitive_data = @EncryptedSensitiveData,
                raw_payload = @RawPayload,
                updated_at = @UpdatedAt,
                next_execution_at = @NextExecutionAt,
                locked_until = @LockedUntil,
                scheduled_at = @ScheduledAt,
                current_attempt = @CurrentAttempt,
                version = version + 1,
                metadata = @Metadata
            WHERE id = @Id AND version = @ExpectedVersion
            RETURNING version";

        var newVersion = await _db.Connection.QuerySingleOrDefaultAsync<int?>(
            sql,
            new
            {
                task.Id,
                task.SenderId,
                task.Type,
                task.Status,
                task.Schedule,
                Strategy = task.Strategy,           // было task.Execution
                ResultDelivery = task.ResultDelivery,
                PollingConfig = task.PollingConfig,
                task.RetryPolicy,
                task.EncryptedSensitiveData,
                task.RawPayload,
                task.UpdatedAt,
                task.NextExecutionAt,
                task.LockedUntil,
                task.ScheduledAt,
                task.CurrentAttempt,
                ExpectedVersion = expectedVersion,
                Metadata = task.Metadata
            },
            transaction: _db.Transaction);

        if (newVersion == null)
            throw new ConcurrencyException(task.Id.Value, expectedVersion);
    }

    public async Task BulkUpdateAsync(IReadOnlyCollection<ScheduledTask> tasks, CancellationToken ct = default)
    {
        foreach (var task in tasks)
        {
            await UpdateAsync(task, task.Version, ct);
        }
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetStaleExecutingTasksAsync(
        DateTime utcNow, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM tasks
            WHERE status = @Status AND locked_until <= @UtcNow
            ORDER BY locked_until ASC
            LIMIT 50
            FOR UPDATE SKIP LOCKED";

        var result = await _db.Connection.QueryAsync<ScheduledTask>(sql, new
        {
            Status = StatusTask.Executing,
            UtcNow = utcNow
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task<ScheduledTask?> TryAcquireQueuedTaskAsync(
        TaskId taskId, DateTime utcNow, int? timeoutSeconds, CancellationToken ct = default)
    {
        var lockDuration = TimeSpan.FromSeconds(timeoutSeconds ?? 30) + TimeSpan.FromSeconds(5);
        var lockedUntil = utcNow + lockDuration;

        const string sql = @"
            UPDATE tasks SET
                status = @NewStatus,
                locked_until = @LockedUntil,
                scheduled_at = next_execution_at,
                next_execution_at = NULL,
                version = version + 1
            WHERE id = @Id 
              AND status = @ExpectedStatus
            RETURNING *";

        var result = await _db.Connection.QuerySingleOrDefaultAsync<ScheduledTask>(
            sql,
            new
            {
                Id = taskId,
                ExpectedStatus = StatusTask.Queued,
                NewStatus = StatusTask.Executing,
                LockedUntil = lockedUntil
            },
            transaction: _db.Transaction);

        return result;
    }

    public async Task<ScheduledTask?> TryAcquirePollingTaskAsync(
        TaskId taskId, DateTime utcNow, TimeSpan lockDuration, CancellationToken ct = default)
    {
        var lockedUntil = utcNow + lockDuration;

        const string sql = @"
            UPDATE tasks SET
                status = @NewStatus,
                locked_until = @LockedUntil,
                next_execution_at = NULL,
                version = version + 1
            WHERE id = @Id 
              AND type = @PollingType
              AND status = @ExpectedStatus
              AND next_execution_at <= @UtcNow
            RETURNING *";

        var result = await _db.Connection.QuerySingleOrDefaultAsync<ScheduledTask>(
            sql,
            new
            {
                Id = taskId,
                PollingType = TaskType.Polling,
                ExpectedStatus = StatusTask.Scheduled,
                NewStatus = StatusTask.Executing,
                LockedUntil = lockedUntil,
                UtcNow = utcNow
            },
            transaction: _db.Transaction);

        return result;
    }

    // Устаревший метод, оставлен для совместимости (не используется)
    public Task<ScheduledTask?> AcquireNextQueuedAsync(CancellationToken ct = default)
    {
        throw new NotSupportedException("Use TryAcquireQueuedTaskAsync instead.");
    }
}
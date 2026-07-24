using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.ValueObjects;
using Infrastructure.Persistence.TypeHandlers;

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
    // Добавляем явное приведение типов для JSONB-полей: ::jsonb
    const string sql = @"
        INSERT INTO tasks (
            id, chain_id, chain_step_index,
            sender_id, idempotency_key,
            type, status,
            schedule, execution,
            result_delivery, polling_config, retry_policy,
            encrypted_sensitive_data, raw_payload,
            created_at, updated_at,
            next_execution_at, locked_until, scheduled_at,
            current_attempt, version, metadata
        ) VALUES (
            @Id, @ChainId, @ChainStepIndex,
            @SenderId, @IdempotencyKey,
            @Type, @Status,
            @Schedule::jsonb, @Strategy::jsonb,
            @ResultDelivery::jsonb, @PollingConfig::jsonb, @RetryPolicy::jsonb,
            @EncryptedSensitiveData, @RawPayload::jsonb,
            @CreatedAt, @UpdatedAt,
            @NextExecutionAt, @LockedUntil, @ScheduledAt,
            @CurrentAttempt, @Version, @Metadata::jsonb
        )";

    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ExecutionStrategyJsonConverter() }
    };

    var parameters = new DynamicParameters();
    parameters.Add("Id", task.Id.Value, DbType.Guid);
    parameters.Add("ChainId", (object?)task.ChainId ?? DBNull.Value, DbType.Guid);
    parameters.Add("ChainStepIndex", (object?)task.ChainStepIndex ?? DBNull.Value, DbType.Int32);
    parameters.Add("SenderId", task.SenderId.Value, DbType.String);
    parameters.Add("IdempotencyKey", task.IdempotencyKey, DbType.String);
    parameters.Add("Type", (int)task.Type, DbType.Int32);
    parameters.Add("Status", (int)task.Status, DbType.Int32);

    // JSONB-поля: сериализуем в JSON-строку, передаём как строку, а в SQL явно приводим к jsonb
    parameters.Add("Schedule", JsonSerializer.Serialize(task.Schedule, jsonOptions), DbType.String);
    parameters.Add("Strategy", JsonSerializer.Serialize(task.Strategy, jsonOptions), DbType.String);
    parameters.Add("ResultDelivery",
        task.ResultDelivery != null ? JsonSerializer.Serialize(task.ResultDelivery, jsonOptions) : DBNull.Value,
        DbType.String);
    parameters.Add("PollingConfig",
        task.PollingConfig != null ? JsonSerializer.Serialize(task.PollingConfig, jsonOptions) : DBNull.Value,
        DbType.String);
    parameters.Add("RetryPolicy", JsonSerializer.Serialize(task.RetryPolicy, jsonOptions), DbType.String);
    parameters.Add("Metadata",
        task.Metadata != null ? JsonSerializer.Serialize(task.Metadata, jsonOptions) : DBNull.Value,
        DbType.String);

    // Остальные параметры
    parameters.Add("EncryptedSensitiveData", (object?)task.EncryptedSensitiveData ?? DBNull.Value, DbType.String);
    parameters.Add("RawPayload", task.RawPayload, DbType.String);
    parameters.Add("CreatedAt", task.CreatedAt, DbType.DateTimeOffset);
    parameters.Add("UpdatedAt", task.UpdatedAt, DbType.DateTimeOffset);
    parameters.Add("NextExecutionAt", (object?)task.NextExecutionAt ?? DBNull.Value, DbType.DateTimeOffset);
    parameters.Add("LockedUntil", (object?)task.LockedUntil ?? DBNull.Value, DbType.DateTimeOffset);
    parameters.Add("ScheduledAt", (object?)task.ScheduledAt ?? DBNull.Value, DbType.DateTimeOffset);
    parameters.Add("CurrentAttempt", task.CurrentAttempt, DbType.Int32);
    parameters.Add("Version", task.Version, DbType.Int32);

    await _db.Connection.ExecuteAsync(sql, parameters, transaction: _db.Transaction);
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
    // В SQL-шаблоне все JSONB-поля принимают строку и явно приводятся к jsonb (::jsonb)
    const string sql = @"
        UPDATE tasks SET
            sender_id = @SenderId,
            type = @Type,
            status = @Status,
            schedule = @Schedule::jsonb,
            execution = @Strategy::jsonb,
            result_delivery = @ResultDelivery::jsonb,
            polling_config = @PollingConfig::jsonb,
            retry_policy = @RetryPolicy::jsonb,
            encrypted_sensitive_data = @EncryptedSensitiveData,
            raw_payload = @RawPayload::jsonb,
            updated_at = @UpdatedAt,
            next_execution_at = @NextExecutionAt,
            locked_until = @LockedUntil,
            scheduled_at = @ScheduledAt,
            current_attempt = @CurrentAttempt,
            version = version + 1,
            metadata = @Metadata::jsonb,
            chain_id = @ChainId,
            chain_step_index = @ChainStepIndex
        WHERE id = @Id AND version = @ExpectedVersion
        RETURNING version";

    // Единые настройки сериализации для JSONB (как в AddAsync)
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ExecutionStrategyJsonConverter() }
    };

    var parameters = new DynamicParameters();
    parameters.Add("Id", task.Id.Value, DbType.Guid);
    parameters.Add("SenderId", task.SenderId.Value, DbType.String);
    parameters.Add("Type", (int)task.Type, DbType.Int32);          // enum -> int
    parameters.Add("Status", (int)task.Status, DbType.Int32);      // enum -> int

    // JSONB-поля: сериализуем в строку и передаём как строку
    parameters.Add("Schedule", JsonSerializer.Serialize(task.Schedule, jsonOptions), DbType.String);
    parameters.Add("Strategy", JsonSerializer.Serialize(task.Strategy, jsonOptions), DbType.String);
    parameters.Add("ResultDelivery",
        task.ResultDelivery != null ? JsonSerializer.Serialize(task.ResultDelivery, jsonOptions) : DBNull.Value,
        DbType.String);
    parameters.Add("PollingConfig",
        task.PollingConfig != null ? JsonSerializer.Serialize(task.PollingConfig, jsonOptions) : DBNull.Value,
        DbType.String);
    parameters.Add("RetryPolicy", JsonSerializer.Serialize(task.RetryPolicy, jsonOptions), DbType.String);
    parameters.Add("Metadata",
        task.Metadata != null ? JsonSerializer.Serialize(task.Metadata, jsonOptions) : DBNull.Value,
        DbType.String);

    parameters.Add("EncryptedSensitiveData", (object?)task.EncryptedSensitiveData ?? DBNull.Value, DbType.String);
    parameters.Add("RawPayload", task.RawPayload, DbType.String);
    parameters.Add("UpdatedAt", task.UpdatedAt, DbType.DateTimeOffset);
    parameters.Add("NextExecutionAt", (object?)task.NextExecutionAt ?? DBNull.Value, DbType.DateTimeOffset);
    parameters.Add("LockedUntil", (object?)task.LockedUntil ?? DBNull.Value, DbType.DateTimeOffset);
    parameters.Add("ScheduledAt", (object?)task.ScheduledAt ?? DBNull.Value, DbType.DateTimeOffset);
    parameters.Add("CurrentAttempt", task.CurrentAttempt, DbType.Int32);
    // Version не передаём — он вычисляется в SQL как version + 1
    parameters.Add("ExpectedVersion", expectedVersion, DbType.Int32);
    parameters.Add("ChainId", (object?)task.ChainId ?? DBNull.Value, DbType.Guid);
    parameters.Add("ChainStepIndex", (object?)task.ChainStepIndex ?? DBNull.Value, DbType.Int32);

    // Выполняем UPDATE и получаем новую версию
    var newVersion = await _db.Connection.QuerySingleOrDefaultAsync<int?>(
        sql, parameters, transaction: _db.Transaction);

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
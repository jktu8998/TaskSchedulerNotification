// Infrastructure/Persistence/Repositories/DapperTaskRepository.cs

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ValueObjects;
using Infrastructure.Persistence.TypeHandlers;
using TaskStatus = Domain.Enums.TaskStatus;

namespace Infrastructure.Persistence.Repositories;

/// <summary>
/// Реализация ITaskRepository на основе Dapper.
/// Зависит от IDbTransactionContext для получения соединения и транзакции.
/// </summary>
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
            id, sender_id, type, status,
            schedule, execution,
            result_delivery, polling_config, retry_policy,
            encrypted_sensitive_data,
            created_at, updated_at,
            next_execution_at, locked_until,
            current_attempt
        ) VALUES (
            @Id, @SenderId, @Type, @Status,
            @Schedule, @Execution,
            @ResultDelivery, @PollingConfig, @RetryPolicy,
            @EncryptedSensitiveData,
            @CreatedAt, @UpdatedAt,
            @NextExecutionAt, @LockedUntil,
            @CurrentAttempt
        )";

        await _db.Connection.ExecuteAsync(sql, new
        {
            task.Id,
            task.SenderId,
            task.Type,
            task.Status,
            task.Schedule,          // <-- сам объект, обработается JsonbTypeHandler<Schedule>
            task.Execution,         // <-- аналогично
            ResultDelivery = task.ResultDelivery,          // может быть null, обработается
            PollingConfig = task.PollingConfig,            // может быть null
            task.RetryPolicy,
            task.EncryptedSensitiveData,
            task.CreatedAt,
            task.UpdatedAt,
            task.NextExecutionAt,
            task.LockedUntil,
            task.CurrentAttempt
        }, transaction: _db.Transaction);
    }
    
    public async Task<ScheduledTask?> GetByIdAsync(TaskId id, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM tasks WHERE id = @Id";
        return await _db.Connection.QuerySingleOrDefaultAsync<ScheduledTask>(
            sql, 
            new { Id = id }, 
            transaction: _db.Transaction
        );
    }
    
    public async Task<IReadOnlyList<ScheduledTask>> GetBySenderIdAsync(
        string senderId, int skip, int take,
        TaskStatus? status = null, TaskType? type = null,
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
            SenderId = senderId,
            Status = status,
            Type = type,
            Skip = skip,
            Take = take
        }, transaction: _db.Transaction);

        return result.AsList();
    }
    
    public async Task<IReadOnlyList<ScheduledTask>> GetScheduledBeforeAsync(DateTime cutoff, CancellationToken ct = default)
    {
        const int batchSize = 100; // ограничиваем размер пачки
        const string sql = @"
        SELECT * FROM tasks 
        WHERE status = @Status AND next_execution_at <= @Cutoff 
        ORDER BY next_execution_at ASC 
        LIMIT @BatchSize 
        FOR UPDATE SKIP LOCKED";

        var result = await _db.Connection.QueryAsync<ScheduledTask>(sql, new
        {
            Status = TaskStatus.Scheduled, // EnumTypeHandler преобразует в int
            Cutoff = cutoff,
            BatchSize = batchSize
        }, transaction: _db.Transaction);

        return result.AsList();
    }

    public async Task UpdateAsync(ScheduledTask task, CancellationToken ct = default)
    {
        const string sql = @"
        UPDATE tasks SET
            sender_id = @SenderId,
            type = @Type,
            status = @Status,
            schedule = @Schedule,
            execution = @Execution,
            result_delivery = @ResultDelivery,
            polling_config = @PollingConfig,
            retry_policy = @RetryPolicy,
            encrypted_sensitive_data = @EncryptedSensitiveData,
            created_at = @CreatedAt,
            updated_at = @UpdatedAt,
            next_execution_at = @NextExecutionAt,
            locked_until = @LockedUntil,
            current_attempt = @CurrentAttempt
        WHERE id = @Id";

        await _db.Connection.ExecuteAsync(sql, new
        {
            task.Id,
            task.SenderId,
            task.Type,
            task.Status,
            task.Schedule,
            task.Execution,
            ResultDelivery = task.ResultDelivery,
            PollingConfig = task.PollingConfig,
            task.RetryPolicy,
            task.EncryptedSensitiveData,
            task.CreatedAt,
            task.UpdatedAt,
            task.NextExecutionAt,
            task.LockedUntil,
            task.CurrentAttempt
        }, transaction: _db.Transaction);
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetStaleExecutingTasksAsync(DateTime utcNow, CancellationToken ct = default)
    {
        const string sql = @"
        SELECT * FROM tasks 
        WHERE status = @Status AND locked_until <= @UtcNow 
        ORDER BY locked_until ASC 
        LIMIT 50 
        FOR UPDATE SKIP LOCKED";

        var result = await _db.Connection.QueryAsync<ScheduledTask>(sql, new
        {
            Status = TaskStatus.Executing,
            UtcNow = utcNow
        }, transaction: _db.Transaction);

        return result.AsList();
    }
    
    public async Task<ScheduledTask?> AcquireNextQueuedAsync(CancellationToken ct = default)
    {
        const string sql = @"
        SELECT * FROM tasks 
        WHERE status = @Status 
        ORDER BY created_at ASC 
        LIMIT 1 
        FOR UPDATE SKIP LOCKED";

        return await _db.Connection.QuerySingleOrDefaultAsync<ScheduledTask>(sql, new
        {
            Status = TaskStatus.Queued
        }, transaction: _db.Transaction);
    }
}
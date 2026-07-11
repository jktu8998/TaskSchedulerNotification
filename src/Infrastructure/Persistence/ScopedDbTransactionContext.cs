// Infrastructure/Persistence/ScopedDbTransactionContext.cs

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Infrastructure.Persistence;

/// <summary>
/// Scoped-реализация IDbTransactionContext.
/// Владеет соединением и транзакцией, автоматически освобождает ресурсы при завершении скоупа.
/// </summary>
internal sealed class ScopedDbTransactionContext : IDbTransactionContext, IAsyncDisposable
{
    private readonly IDbConnectionFactory _factory;
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _disposed;

    public IDbConnection Connection =>
        _connection ?? throw new InvalidOperationException("Transaction not started. Call BeginTransactionAsync first.");

    public IDbTransaction? Transaction => _transaction;

    public ScopedDbTransactionContext(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_connection != null)
            throw new InvalidOperationException("Transaction already started.");

        _connection = _factory.CreateConnection();
        await _connection.OpenAsync(ct);
        _transaction = await _connection.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction.");

        await _transaction.CommitAsync(ct);
        await CleanupAsync();
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction.");

        await _transaction.RollbackAsync(ct);
        await CleanupAsync();
    }

    private async Task CleanupAsync()
    {
        if (_transaction != null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Если транзакция всё ещё активна при завершении скоупа – откатываем
        if (_transaction != null)
        {
            try { await _transaction.RollbackAsync(); } catch { /* игнорируем */ }
        }
        await CleanupAsync();
    }
}
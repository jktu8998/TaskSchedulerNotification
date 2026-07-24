
using System.Data;
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

    // Признак, что транзакция была начата явно (через BeginTransactionAsync)
    private bool _transactionStarted;

    public IDbConnection Connection
    {
        get
        {
            // Ленивое открытие соединения, если ещё не открыто
            if (_connection == null)
            {
                _connection = _factory.CreateConnection();
                _connection.Open(); // синхронное открытие безопасно в провайдере Npgsql
            }
            return _connection;
        }
    }
    public IDbTransaction? Transaction => _transaction;

    public ScopedDbTransactionContext(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        // Если транзакция уже начата — ничего не делаем (идемпотентность)
        if (_transaction != null)
            return;

        // Убедимся, что соединение открыто (если ещё нет — откроем)
        if (_connection == null)
        {
            _connection = _factory.CreateConnection();
            await _connection.OpenAsync(ct);
        }
        else if (_connection.State != System.Data.ConnectionState.Open)
        {
            // маловероятно, но на всякий случай
            await _connection.OpenAsync(ct);
        }

        _transaction = await _connection.BeginTransactionAsync(ct);
        _transactionStarted = true;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        await _transaction.CommitAsync(ct);
        await CleanupAsync();
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");

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

        // Если есть активная транзакция (не закоммиченная) — откатываем
        if (_transaction != null)
        {
            try { await _transaction.RollbackAsync(); } catch { /* игнорируем */ }
            await _transaction.DisposeAsync();
        }

        if (_connection != null)
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        ;
    }
}
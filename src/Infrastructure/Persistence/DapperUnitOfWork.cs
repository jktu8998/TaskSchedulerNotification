// Infrastructure/Persistence/DapperUnitOfWork.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces;

namespace Infrastructure.Persistence;

/// <summary>
/// Реализация IUnitOfWork, делегирующая управление транзакцией в IDbTransactionContext.
/// </summary>
public sealed class DapperUnitOfWork : IUnitOfWork
{
    private readonly IDbTransactionContext _dbContext;

    public DapperUnitOfWork(IDbTransactionContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task BeginTransactionAsync(CancellationToken ct = default) =>
        _dbContext.BeginTransactionAsync(ct);

    public Task CommitAsync(CancellationToken ct = default) =>
        _dbContext.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct = default) =>
        _dbContext.RollbackAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (_dbContext is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}
using Application.Interfaces;
using Domain.Interfaces;

namespace Infrastructure.Persistence;

/// <summary>
/// Реализация IUnitOfWork, делегирующая управление транзакцией в IDbTransactionContext.
/// </summary>
public sealed class DapperUnitOfWork : IUnitOfWork
{
    private readonly IDbTransactionContext _dbContext;
    private readonly List<IHasDomainEvents> _trackedAggregates = new();

    public DapperUnitOfWork(IDbTransactionContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Track(IHasDomainEvents aggregate)
    {
        if (aggregate != null)
            _trackedAggregates.Add(aggregate);
    }

    public Task BeginTransactionAsync(CancellationToken ct = default) =>
        _dbContext.BeginTransactionAsync(ct);

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _dbContext.CommitAsync(ct);
        // Очищаем доменные события у всех отслеженных агрегатов
        foreach (var aggregate in _trackedAggregates)
        {
            aggregate.ClearDomainEvents();
        }
        _trackedAggregates.Clear();
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _dbContext.RollbackAsync(ct);
        _trackedAggregates.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_dbContext is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}
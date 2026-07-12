
using System.Data;


namespace Infrastructure.Persistence;

/// <summary>
/// Предоставляет доступ к текущему соединению и транзакции в рамках скоупа.
/// Используется репозиториями для выполнения SQL-запросов.
/// </summary>
public interface IDbTransactionContext
{
    /// <summary>Открытое соединение с БД (не null после вызова BeginTransaction).</summary>
    IDbConnection Connection { get; }

    /// <summary>Текущая транзакция или null, если транзакция не начата.</summary>
    IDbTransaction? Transaction { get; }

    /// <summary>Начинает новую транзакцию (открывает соединение).</summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Фиксирует транзакцию и закрывает соединение.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Откатывает транзакцию и закрывает соединение.</summary>
    Task RollbackAsync(CancellationToken ct = default);
}
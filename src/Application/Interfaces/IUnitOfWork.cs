

using Domain.Interfaces;

namespace Application.Interfaces;

/// <summary>
/// Абстракция над Unit of Work для управления транзакциями БД.
/// Наследует IAsyncDisposable: при выходе из using-блока, если Commit не вызван,
/// транзакция автоматически откатывается.
/// Используется Command Handler'ами для атомарного сохранения агрегатов и логов.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Начать транзакцию.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Зафиксировать транзакцию (сохранить все изменения).
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Откатить транзакцию в случае ошибки.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Зарегистрировать агрегат для отслеживания доменных событий.
    /// После успешного коммита транзакции вызывается ClearDomainEvents().
    /// </summary>
    /// <param name="aggregate">Агрегат, реализующий IHasDomainEvents.</param>
    void Track(IHasDomainEvents aggregate);
}
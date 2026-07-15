namespace Domain.Interfaces;

/// <summary>
/// Контракт для агрегатов, поддерживающих накопление и очистку доменных событий.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Очистить список накопленных доменных событий.</summary>
    void ClearDomainEvents();
}
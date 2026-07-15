namespace Application.Interfaces;

/// <summary>
/// Маркерный интерфейс для команд, требующих транзакционной обработки.
/// Декоратор автоматически открывает транзакцию, вызывает хендлер, коммитит и очищает доменные события.
/// </summary>
public interface ITransactionalCommand { }
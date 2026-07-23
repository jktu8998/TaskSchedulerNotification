namespace Application.Interfaces;

/// <summary>
/// Маркерный интерфейс для команд, чьи обработчики самостоятельно управляют транзакциями.
/// Декоратор TransactionCommandHandlerDecorator не будет открывать транзакцию для таких команд.
/// </summary>
public interface IManagesOwnTransaction { }
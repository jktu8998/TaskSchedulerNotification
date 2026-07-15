namespace Application.Interfaces;

/// <summary>
/// Маркерный интерфейс для команды без результата.
/// </summary>
public interface ICommand { }

/// <summary>
/// Маркерный интерфейс для команды с результатом.
/// </summary>
public interface ICommand<TResult> { }
namespace Application.Interfaces;

/// <summary>
/// Контекст текущего запроса. Содержит идентификатор сервиса-отправителя,
/// извлечённый из аутентификационного токена.
/// </summary>
public interface IRequestContext
{
    string SenderId { get; }
}
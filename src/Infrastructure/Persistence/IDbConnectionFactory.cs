// Infrastructure/Persistence/IDbConnectionFactory.cs
using System.Data;
using Npgsql;

namespace Infrastructure.Persistence;

/// <summary>
/// Фабрика для создания новых экземпляров NpgsqlConnection.
/// Не открывает соединение — это задача вызывающего кода (Unit of Work).
/// </summary>
public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}
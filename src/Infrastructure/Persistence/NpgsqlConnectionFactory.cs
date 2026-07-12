using Npgsql;

namespace Infrastructure.Persistence;

/// <summary>
/// Реализация IDbConnectionFactory, создающая NpgsqlConnection по строке подключения.
/// </summary>
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        
    }

    /// <summary>
    /// Создаёт новое закрытое соединение с PostgreSQL.
    /// </summary>
    public NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }
}
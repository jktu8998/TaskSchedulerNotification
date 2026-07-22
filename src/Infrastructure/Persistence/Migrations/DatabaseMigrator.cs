using System.Reflection;
using DbUp;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Выполняет миграции базы данных PostgreSQL при старте приложения.
/// Использует DbUp: скрипты хранятся как Embedded Resources в сборке.
/// Если база данных не существует, DbUp создаёт её автоматически.
/// Каждый скрипт выполняется внутри явной транзакции.
/// </summary>
public static class DatabaseMigrator
{
    public static void RunMigrations(string connectionString, ILogger logger)
    {
        logger.LogInformation("Starting database migration...");
        // Автоматическое создание базы данных, если её нет
        EnsureDatabase.For.PostgresqlDatabase(connectionString);
       
        // Адаптер для интеграции DbUp с Microsoft.Extensions.Logging (через Serilog)
        var dbUpLogger = new DbUpLoggerAdapter(logger);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogTo(dbUpLogger)                    // используем адаптер
            .WithTransactionPerScript()           // каждый скрипт в своей транзакции
            .Build();

        var scripts = upgrader.GetScriptsToExecute();
        logger.LogInformation("Found {Count} migration scripts to execute", scripts.Count);
        foreach (var script in scripts)
            logger.LogInformation("Script: {Name}", script.Name);

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            logger.LogError(result.Error, "Database migration failed");
            throw result.Error;
        }

        logger.LogInformation("Database migration completed successfully");
    }

    /// <summary>
    /// Адаптер, связывающий DbUp IUpgradeLog и Microsoft.Extensions.Logging.ILogger.
    /// </summary>
    private sealed class DbUpLoggerAdapter : IUpgradeLog
    {
        private readonly ILogger _logger;

        public DbUpLoggerAdapter(ILogger logger) => _logger = logger;

        public void LogInformation(string format, params object[] args) =>
            _logger.LogInformation(format, args);

        public void LogError(string format, params object[] args) =>
            _logger.LogError(format, args);

        public void LogError(Exception ex, string format, params object[] args) =>
            _logger.LogError(ex, format, args);

        public void LogWarning(string format, params object[] args) =>
            _logger.LogWarning(format, args);

        public void LogDebug(string format, params object[] args) =>
            _logger.LogDebug(format, args);

        public void LogTrace(string format, params object[] args) =>
            _logger.LogTrace(format, args);
    }
}
// Infrastructure/DependencyInjection.cs

using System;
using Application.Handlers;
using Application.Interfaces;
using Domain.Interfaces;
using Infrastructure.BackgroundServices;
using Infrastructure.Messaging;
using Infrastructure.Network;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Persistence.TypeHandlers;
using Infrastructure.SystemUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

/// <summary>
/// Регистрация всех инфраструктурных зависимостей в DI-контейнере.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --------------------------------------------------
        // 1. Dapper Type Handlers (один раз при старте)
        // --------------------------------------------------
        DapperTypeHandlerConfig.Register();

        // --------------------------------------------------
        // 2. Persistence
        // --------------------------------------------------
        // Фабрика подключений (Singleton, stateless)
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");
        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        // Контекст транзакции (Scoped – одно соединение/транзакция на запрос/воркер)
        services.AddScoped<IDbTransactionContext, ScopedDbTransactionContext>();

        // Unit of Work (Scoped)
        services.AddScoped<IUnitOfWork, DapperUnitOfWork>();

        // Репозитории (Scoped)
        services.AddScoped<ITaskRepository, DapperTaskRepository>();
        services.AddScoped<IOutboxRepository, DapperOutboxRepository>();
        services.AddScoped<ITaskLogRepository, DapperTaskLogRepository>();
        services.AddScoped<IDeadLetterRepository, DapperDeadLetterRepository>();
        services.AddScoped<IPollingStateRepository, DapperPollingStateRepository>();

        // --------------------------------------------------
        // 3. System Utilities (Singleton)
        // --------------------------------------------------
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IRandomProvider, SystemRandomProvider>();

        // Шифрование (Singleton, ключ из конфигурации)
        var encryptionKey = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption key is required.");
        services.AddSingleton<IEncryptionService>(new AesEncryptionService(encryptionKey));

        // --------------------------------------------------
        // 4. Network
        // --------------------------------------------------
        services.AddHttpClient("TaskExecutor"); // IHttpClientFactory
        services.AddSingleton<IHttpExecutor, HttpClientExecutor>();

        // --------------------------------------------------
        // 5. Messaging (RabbitMQ)
        // --------------------------------------------------
        var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException("RabbitMQ connection string is required.");
        // Регистрируем IMessageQueue как Singleton – ленивое подключение внутри
        services.AddSingleton<IMessageQueue>(new RabbitMqMessageQueue(rabbitMqConnectionString));

        // --------------------------------------------------
        // 6. Background Services
        // --------------------------------------------------
        services.AddHostedService<OutboxProcessorWorker>();
        services.AddHostedService<SchedulerWorker>();
        services.AddHostedService<HeartbeatWorker>();
        services.AddHostedService<TaskExecutionWorker>();

        return services;
    }
}
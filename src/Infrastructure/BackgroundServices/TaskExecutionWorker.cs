// Infrastructure/BackgroundServices/TaskExecutionWorker.cs

using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Commands;
using Application.Handlers;
using Application.Interfaces; // Для ICommandHandler
using Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Infrastructure.BackgroundServices;

public sealed class TaskExecutionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskExecutionWorker> _logger;
    private readonly string _rabbitMqConnectionString;

    public TaskExecutionWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<TaskExecutionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _rabbitMqConnectionString = configuration.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException("RabbitMQ connection string not configured");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TaskExecutionWorker запущен");

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitMqConnectionString),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        // ИСПРАВЛЕНИЕ: Используем await using для корректного асинхронного освобождения ресурсов в v7
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false
        );
        await using var channel = await connection.CreateChannelAsync(options, stoppingToken);

        await channel.QueueDeclareAsync(
            queue: "task_execution_queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Гарантируем, что воркер берет только 1 задачу за раз
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                
                // ИСПРАВЛЕНИЕ: Убираем кавычки от JSON-сериализации перед парсингом
                var cleanMessage = message.Trim('"');
                
                if (!Guid.TryParse(cleanMessage, out var taskGuid))
                {
                    _logger.LogWarning("Получено некорректное сообщение: {Message}", message);
                    // Ошибочный формат - удаляем из очереди (requeue: false неявно в BasicAck/Reject)
                    await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                    return;
                }

                var taskId = TaskId.From(taskGuid);
                _logger.LogDebug("Получено задание {TaskId}", taskId.Value);

                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunExecutionCommand>>();

                // Запускаем выполнение
                await handler.HandleAsync(new RunExecutionCommand(taskId.Value), stoppingToken);

                // Если всё прошло без исключений, подтверждаем обработку
                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                _logger.LogDebug("Задание {TaskId} успешно обработано", taskId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки задания из очереди");
                
                // ИСПРАВЛЕНИЕ: Пауза 1 секунда, чтобы защититься от бесконечного цикла Poison Message
                try { await Task.Delay(1000, stoppingToken); } catch { /* игнор */ }
                
                // Возвращаем задачу в очередь
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: "task_execution_queue",
            autoAck: false, // Важно! Ручное подтверждение
            consumer: consumer,
            cancellationToken: stoppingToken);

        // ИСПРАВЛЕНИЕ: Безопасное ожидание остановки приложения
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Штатная остановка приложения
        }

        _logger.LogInformation("TaskExecutionWorker остановлен");
    }
}
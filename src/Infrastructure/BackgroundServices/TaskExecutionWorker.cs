using System.Text;
using System.Text.Json;
using Application.Commands;
using Application.Interfaces;
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

    // Структура сообщения для расширяемости
    private sealed record TaskExecutionMessage(Guid TaskId);

    public TaskExecutionWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TaskExecutionWorker> logger)
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

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var channel = await connection.CreateChannelAsync(options, stoppingToken);

        await channel.QueueDeclareAsync(
            queue: "task_execution_queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);

                // Десериализуем JSON-сообщение
                var message = JsonSerializer.Deserialize<TaskExecutionMessage>(json);
                if (message is null)
                {
                    _logger.LogWarning("Получено некорректное сообщение: {Json}", json);
                    await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                    return;
                }

                var taskId = TaskId.From(message.TaskId);
                _logger.LogDebug("Получено задание {TaskId}", taskId.Value);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunExecutionCommand>>();

                await handler.HandleAsync(new RunExecutionCommand(taskId.Value), stoppingToken);
                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                _logger.LogDebug("Задание {TaskId} успешно обработано", taskId.Value);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Некорректный JSON в сообщении. Сообщение удалено из очереди.");
                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки задания из очереди");
                try { await Task.Delay(1000, stoppingToken); } catch { /* игнор */ }
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: "task_execution_queue",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Штатная остановка
        }

        _logger.LogInformation("TaskExecutionWorker остановлен");
    }
}

using System.Text;
using Application.Interfaces;
using Domain.ValueObjects;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Infrastructure.Messaging;

/// <summary>
/// Реализация IMessageQueue на базе RabbitMQ (Client v7.x).
/// Ленивое подключение: устанавливает связь только при первой попытке публикации.
/// </summary>
public sealed class RabbitMqMessageQueue : IMessageQueue, IAsyncDisposable
{
    private const string QueueName = "task_execution_queue";
    private readonly ConnectionFactory _factory;

    private IConnection? _connection;
    private IChannel? _channel;
    
    // Защита от создания нескольких подключений при конкурентных запросах
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public RabbitMqMessageQueue(string connectionString)
    {
        // Конструктор синхронный и безопасный для DI
        _factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_channel != null) return;

        await _connectionLock.WaitAsync(ct);
        try
        {
            // Double-check locking
            if (_channel != null) return;

            _connection = await _factory.CreateConnectionAsync(ct);
            
            // Твоя идеальная конфигурация для v7
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            );

            _channel = await _connection.CreateChannelAsync(channelOptions, ct);

            await _channel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task PublishScheduledTaskAsync(TaskId taskId, CancellationToken ct = default)
    {
        // Подключаемся только в момент отправки
        await EnsureConnectedAsync(ct);

        // Маленькая оптимизация: ToString() для Guid быстрее и чище, чем сериализация в JSON
        var message = taskId.Value.ToString();
        var body = Encoding.UTF8.GetBytes(message);

        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent 
        };

        try
        {
            // Библиотека сама дождется ACK благодаря включенному трекингу
            await _channel!.BasicPublishAsync(
                exchange: "",
                routingKey: QueueName,
                mandatory: true,
                basicProperties: props,
                body: body,
                cancellationToken: ct);
        }
        catch (PublishException ex)
        {
            // Сюда мы попадем, если брокер вернул NACK или сообщение не дошло (аналог "OrDie")
            throw new InvalidOperationException($"Сообщение не было подтверждено брокером RabbitMQ: {ex.Message}", ex);
        }
    }
    
     
    public Task SubscribeAsync(Func<TaskId, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        // TODO: реализовать асинхронного потребителя с помощью AsyncEventingBasicConsumer
        throw new NotImplementedException();
    }

    public async ValueTask DisposeAsync()
    {
        // В v7.x вызов CloseAsync перед Dispose больше не требуется, 
        // так как DisposeAsync выполняет корректное закрытие ресурсов.
        if (_channel != null)
        {
            await _channel.DisposeAsync();
        }
        
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }

        _connectionLock.Dispose();
    }
}
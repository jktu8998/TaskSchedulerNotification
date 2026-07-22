using System.Text.Json;
using Application.Interfaces;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

public sealed class OutboxProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorWorker> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMilliseconds(500);

    public OutboxProcessorWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessorWorker запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();
                var httpExecutor = scope.ServiceProvider.GetRequiredService<IHttpExecutor>();

                // 1. Открываем транзакцию. Это критично для FOR UPDATE SKIP LOCKED
                await uow.BeginTransactionAsync(stoppingToken);

                // Забираем пачку сообщений. Транзакция держит блокировку строк в PostgreSQL
                var messages = await outboxRepo.GetUnprocessedAsync(batchSize: 50, stoppingToken);

                // 2. Если сообщений нет, фиксируем транзакцию, спим и идем на следующий круг
                if (messages.Count == 0)
                {
                    await uow.CommitAsync(stoppingToken);
                    await Task.Delay(_pollingInterval, stoppingToken);
                    continue;
                }

                // 3. Обрабатываем сообщения
                foreach (var message in messages)
                {
                    try
                    {
                        bool processed = message.EventType switch
                        {
                            "TaskQueuedEvent" or "TaskScheduledEvent" => await PublishToQueueAsync(message, messageQueue, stoppingToken),
                            "ResultDeliveryRequested" => await DeliverResultAsync(message, httpExecutor, stoppingToken),
                            _ => false
                        };

                        if (processed)
                        {
                            await outboxRepo.RemoveAsync(message.Id, stoppingToken);
                            _logger.LogDebug("Outbox-сообщение {OutboxId} обработано", message.Id);
                        }
                        else
                        {
                            // Неудача – увеличиваем счётчик попыток
                            if (message.TryIncrementRetry())
                            {
                                await outboxRepo.UpdateAsync(message, stoppingToken);
                                _logger.LogWarning("Повторная попытка для Outbox {OutboxId}", message.Id);
                            }
                            else
                            {
                                // Лимит исчерпан – удаляем без выполнения
                                await outboxRepo.RemoveAsync(message.Id, stoppingToken);
                                _logger.LogError("Outbox-сообщение {OutboxId} не обработано, лимит попыток исчерпан", message.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка обработки Outbox-сообщения {OutboxId}", message.Id);
                    }
                }

                await uow.CommitAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // штатная остановка
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в цикле OutboxProcessorWorker");
                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }

        _logger.LogInformation("OutboxProcessorWorker остановлен");
    }

    private async Task<bool> PublishToQueueAsync(Domain.Entities.OutboxMessage message, IMessageQueue queue, CancellationToken ct)
    {
        try
        {
            await queue.PublishScheduledTaskAsync(message.TaskId, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось опубликовать задание {TaskId} в очередь", message.TaskId);
            return false;
        }
    }

    private async Task<bool> DeliverResultAsync(Domain.Entities.OutboxMessage message, IHttpExecutor executor, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message.Payload)) return false;

            // Десериализуем параметры доставки
            var delivery = JsonSerializer.Deserialize<ResultDeliveryPayload>(message.Payload);
            if (delivery == null) return false;

            var config = new HttpRequestConfig(delivery.Method, delivery.Url, null, delivery.Body);
            var response = await executor.ExecuteAsync(config, ct);
            return response.IsSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка доставки результата для задания {TaskId}", message.TaskId);
            return false;
        }
    }

    // Вспомогательный класс для десериализации payload доставки
    private sealed record ResultDeliveryPayload(string Mode, string Url, string Method, string? Body);
}
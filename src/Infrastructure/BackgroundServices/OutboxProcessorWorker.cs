// Infrastructure/BackgroundServices/OutboxProcessorWorker.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

/// <summary>
/// Фоновый воркер, который забирает необработанные сообщения из таблицы Outbox,
/// публикует их в RabbitMQ и удаляет после успешной отправки.
/// </summary>
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
                using var scope = _scopeFactory.CreateScope();
                
                // Достаем UnitOfWork для управления транзакцией
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();

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
                        // Публикуем в RabbitMQ (в v7.x метод ждет ACK)
                        await messageQueue.PublishScheduledTaskAsync(message.TaskId, stoppingToken);

                        // Успешно — помечаем на удаление в рамках текущей транзакции БД
                        await outboxRepo.RemoveAsync(message.Id, stoppingToken);

                        _logger.LogDebug("Сообщение {OutboxId} для задания {TaskId} отправлено", message.Id, message.TaskId);
                    }
                    catch (Exception ex)
                    {
                        // Если RabbitMQ недоступен или вернул NACK, логируем ошибку.
                        // Метод RemoveAsync не вызовется, сообщение останется в БД.
                        _logger.LogWarning(ex, "Не удалось обработать Outbox-сообщение {OutboxId}", message.Id);
                    }
                }

                // 4. Фиксируем удаления (или откатываем, если всё упало). 
                // Блокировка FOR UPDATE снимается именно здесь.
                await uow.CommitAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Игнорируем исключение при штатной остановке приложения
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в цикле OutboxProcessorWorker");
                // Пауза перед ретраем, чтобы при падении БД не спамить логи тысячами ошибок в секунду
                await Task.Delay(_pollingInterval, stoppingToken); 
            }
        }

        _logger.LogInformation("OutboxProcessorWorker остановлен");
    }
}
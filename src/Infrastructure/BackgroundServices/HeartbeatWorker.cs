
using Application.Commands;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

/// <summary>
/// Фоновый воркер, периодически запускающий механизм Heartbeat.
/// Находит задания в статусе Executing с истекшим LockedUntil,
/// помечает их как Failed и перепланирует, либо отправляет в Dead Letter Queue.
/// </summary>
public sealed class HeartbeatWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public HeartbeatWorker(IServiceScopeFactory scopeFactory, ILogger<HeartbeatWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatWorker запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunHeartbeatCommand>>();

                await handler.HandleAsync(new RunHeartbeatCommand(), stoppingToken);
                // Delay теперь надежно спрятан внутри try
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // штатная остановка
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в HeartbeatWorker");
                // Если база отвалилась, спим те же 30 секунд перед новой попыткой
                try { await Task.Delay(_interval, stoppingToken); } catch { /* игнор */ }
            }

        }

        _logger.LogInformation("HeartbeatWorker остановлен");
    }
}
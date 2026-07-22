using Application.Commands;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

public sealed class PollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PollingWorker> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(15);

    public PollingWorker(IServiceScopeFactory scopeFactory, ILogger<PollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PollingWorker запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunPollingCheckCommand>>();

                await handler.HandleAsync(new RunPollingCheckCommand(), stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // штатная остановка
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в PollingWorker");
                try { await Task.Delay(_pollingInterval, stoppingToken); } catch { /* игнор */ }
            }
        }

        _logger.LogInformation("PollingWorker остановлен");
    }
}
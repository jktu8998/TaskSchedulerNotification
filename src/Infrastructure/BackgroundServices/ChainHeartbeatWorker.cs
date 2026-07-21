using Application.Commands;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

public sealed class ChainHeartbeatWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChainHeartbeatWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public ChainHeartbeatWorker(IServiceScopeFactory scopeFactory, ILogger<ChainHeartbeatWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChainHeartbeatWorker запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunChainHeartbeatCommand>>();
                await handler.HandleAsync(new RunChainHeartbeatCommand(), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в ChainHeartbeatWorker");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // штатная остановка
            }
        }
        _logger.LogInformation("ChainHeartbeatWorker остановлен");
    }
}
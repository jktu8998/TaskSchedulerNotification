
using Application.Commands;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

public sealed class SchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerWorker> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    public SchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<SchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SchedulerWorker запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunSchedulingCommand>>();

                await handler.HandleAsync(new RunSchedulingCommand(), stoppingToken);

                // Delay теперь внутри try. Если придет сигнал остановки во время сна, 
                // мы безопасно попадем в блок catch (TaskCanceledException)
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Игнорируем штатную остановку приложения
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в SchedulerWorker");
                // Пауза перед ретраем в случае ошибки (например, БД недоступна)
                // Используем try-catch внутри catch на случай, если отмена придет во время этого сна
                try { await Task.Delay(_pollingInterval, stoppingToken); } catch { /* игнор */ }
            }
        }

        _logger.LogInformation("SchedulerWorker остановлен");
    }
}
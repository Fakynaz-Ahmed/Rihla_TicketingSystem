using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rihla.Config;

namespace Rihla.Services.ErpNext;

public class ErpNextSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ErpNextSyncBackgroundService> _logger;
    private readonly ErpNextSettings _settings;

    public ErpNextSyncBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<ErpNextSettings> settings,
        ILogger<ErpNextSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableBackgroundSync)
        {
            _logger.LogInformation("ERPNext background user synchronization is disabled via configuration.");
            return;
        }

        _logger.LogInformation("ERPNext background user synchronization started. Interval: {Hours} hours.", _settings.SyncIntervalHours);

        // Run immediately on startup, then wait for the interval
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Triggering scheduled ERPNext user synchronization...");
                using (var scope = _serviceProvider.CreateScope())
                {
                    var syncService = scope.ServiceProvider.GetRequiredService<IErpNextSyncService>();
                    var result = await syncService.SyncUsersAsync();
                    if (result.Success)
                    {
                        _logger.LogInformation("Scheduled ERPNext user synchronization completed: {Message}", result.Message);
                    }
                    else
                    {
                        _logger.LogWarning("Scheduled ERPNext user synchronization failed: {Message}", result.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during scheduled ERPNext user synchronization.");
            }

            var delayHours = _settings.SyncIntervalHours <= 0 ? 6 : _settings.SyncIntervalHours;
            var delayDuration = TimeSpan.FromHours(delayHours);
            _logger.LogInformation("Next background sync scheduled in {Hours} hours.", delayHours);
            
            try
            {
                await Task.Delay(delayDuration, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
        }
    }
}

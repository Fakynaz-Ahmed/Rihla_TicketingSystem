using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rihla.Services.Db;

public class MongoIndexInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MongoIndexInitializer> _logger;

    public MongoIndexInitializer(IServiceScopeFactory scopeFactory, ILogger<MongoIndexInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
            await ((MessageRepository)repo).EnsureIndexesAsync();
            _logger.LogInformation("MongoDB indexes ensured.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MongoDB indexes on startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

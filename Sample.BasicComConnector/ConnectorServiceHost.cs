using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Windows Service host for the PlaceiT simulator connector
/// </summary>
public class ConnectorServiceHost : BackgroundService
{
    private readonly ILogger<ConnectorServiceHost> _logger;

    public ConnectorServiceHost(ILogger<ConnectorServiceHost> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PlaceiT Connector Service starting");

        try
        {
            // Initialize and run the connector
            await ConnectorRuntime.RunStandalone().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in connector service");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PlaceiT Connector Service stopping");
        await base.StopAsync(cancellationToken);
    }
}


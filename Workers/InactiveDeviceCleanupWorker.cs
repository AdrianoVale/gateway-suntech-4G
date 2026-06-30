using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Workers;

public sealed class InactiveDeviceCleanupWorker : BackgroundService
{
    private readonly ILogger<InactiveDeviceCleanupWorker> _logger;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IGatewayMetrics _metrics;
    private readonly GatewayOptions _options;
    private readonly TimeProvider _timeProvider;

    public InactiveDeviceCleanupWorker(
        ILogger<InactiveDeviceCleanupWorker> logger,
        IDeviceRegistry deviceRegistry,
        ICommandRegistry commandRegistry,
        IGatewayMetrics metrics,
        IOptions<GatewayOptions> options,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _deviceRegistry = deviceRegistry;
        _commandRegistry = commandRegistry;
        _metrics = metrics;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cleanupInterval = TimeSpan.FromSeconds(_options.Devices.CleanupIntervalSeconds);
        var inactiveAfter = TimeSpan.FromSeconds(_options.Devices.InactiveAfterSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cleanupInterval, stoppingToken);
                var now = _timeProvider.GetUtcNow();
                var removed = _deviceRegistry.RemoveInactive(inactiveAfter, now);
                var removedCommands = _commandRegistry.RemoveInactive(inactiveAfter, now);
                _metrics.SetDevicesConnected(_deviceRegistry.Count);
                _metrics.SetCommandsPending(_commandRegistry.Count);
                if (removed > 0)
                {
                    _logger.LogInformation("Limpeza de devices inativos removeu {Removed} item(ns)", removed);
                }
                if (removedCommands > 0)
                {
                    _logger.LogInformation("Limpeza de comandos inativos removeu {RemovedCommands} item(ns)", removedCommands);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na rotina de limpeza de devices inativos");
            }
        }
    }
}
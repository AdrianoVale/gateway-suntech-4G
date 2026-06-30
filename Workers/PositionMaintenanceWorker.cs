using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Workers;

public sealed class PositionMaintenanceWorker : BackgroundService
{
    private readonly ILogger<PositionMaintenanceWorker> _logger;
    private readonly IGatewayDataService _dataService;
    private readonly GatewayOptions _options;

    public PositionMaintenanceWorker(
        ILogger<PositionMaintenanceWorker> logger,
        IGatewayDataService dataService,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _dataService = dataService;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PositionMaintenance.CheckIntervalSeconds), stoppingToken);
                var total = _dataService.GetTotalPositionCount();
                if (total > _options.PositionMaintenance.CleanupThreshold)
                {
                    _logger.LogInformation("Limpeza de posições iniciada; total atual={Total}", total);
                    _dataService.CleanupPositions();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na rotina de manutenção de posições");
            }
        }
    }
}
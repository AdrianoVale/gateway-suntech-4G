using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Workers;

public sealed class ReplayWorker : BackgroundService
{
    private readonly ILogger<ReplayWorker> _logger;
    private readonly IPositionPersistenceService _positionPersistenceService;
    private readonly GatewayOptions _options;

    public ReplayWorker(
        ILogger<ReplayWorker> logger,
        IPositionPersistenceService positionPersistenceService,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _positionPersistenceService = positionPersistenceService;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReplayWorker iniciado. Verificando cache em disco...");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = _positionPersistenceService.PendingCacheCount();
                if (pending > 0)
                {
                    _logger.LogInformation("ReplayWorker encontrou {Pending} arquivo(s) pendente(s). Iniciando replay...", pending);
                    _positionPersistenceService.ReplayPending();
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.Replay.CheckIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no ReplayWorker; nova tentativa em {RetrySeconds}s", _options.Replay.DbRetryIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(_options.Replay.DbRetryIntervalSeconds), stoppingToken);
            }
        }
    }
}
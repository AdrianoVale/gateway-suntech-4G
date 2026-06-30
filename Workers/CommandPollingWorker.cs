using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Workers;

public sealed class CommandPollingWorker : BackgroundService
{
    private readonly ILogger<CommandPollingWorker> _logger;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly GatewayOptions _options;
    private bool _firstPoll = true;

    public CommandPollingWorker(
        ILogger<CommandPollingWorker> logger,
        ICommandDispatcher commandDispatcher,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _commandDispatcher = commandDispatcher;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Commands.PollIntervalMilliseconds, stoppingToken);
                _commandDispatcher.PollAndDispatch(_firstPoll);
                _firstPoll = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na rotina de polling de comandos");
            }
        }
    }
}
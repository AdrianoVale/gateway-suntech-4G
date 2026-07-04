using GatewaySunteh4G_NET8.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace GatewaySunteh4G_NET8.Services;

/// <summary>
/// Publica atualizações de status de comando via SignalR para o grupo do dispositivo.
/// </summary>
public sealed class CommandHubPublisher : ICommandHubPublisher
{
    private readonly IHubContext<PositionHub> _hub;
    private readonly ILogger<CommandHubPublisher> _logger;

    public CommandHubPublisher(IHubContext<PositionHub> hub, ILogger<CommandHubPublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishAsync(string deviceId, int commandId, int statusId)
    {
        var group = PositionHub.GroupName(deviceId);
        var situacao = MapStatusText(statusId);

        _logger.LogDebug(
            "[CommandHubPublisher] Enviando AtualizacaoComando para grupo {Group}. deviceId={DeviceId}, commandId={CommandId}, statusId={StatusId}, situacao={Situacao}",
            group,
            deviceId,
            commandId,
            statusId,
            situacao);

        try
        {
            await _hub.Clients.Group(group).SendAsync("AtualizacaoComando", new
            {
                commandId,
                deviceId,
                statusId,
                situacao
            });

            _logger.LogDebug(
                "[CommandHubPublisher] AtualizacaoComando enviado com sucesso para grupo {Group}. commandId={CommandId}",
                group,
                commandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[CommandHubPublisher] Falha ao enviar AtualizacaoComando para grupo {Group}. deviceId={DeviceId}, commandId={CommandId}, statusId={StatusId}",
                group,
                deviceId,
                commandId,
                statusId);
            throw;
        }
    }

    private static string MapStatusText(int statusId) => statusId switch
    {
        1 => "Pendente",
        2 => "Confirmado",
        3 => "Enviado",
        4 => "Falha",
        5 => "Cancelando",
        6 => "Cancelado",
        _ => "Desconhecido"
    };
}

/// <summary>
/// Implementação no-op de ICommandHubPublisher usada quando Hub está desativado.
/// </summary>
public sealed class NullCommandHubPublisher : ICommandHubPublisher
{
    public Task PublishAsync(string deviceId, int commandId, int statusId) => Task.CompletedTask;
}

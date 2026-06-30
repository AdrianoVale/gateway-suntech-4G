using GatewaySunteh4G_NET8.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GatewaySunteh4G_NET8.Services;

/// <summary>
/// Publica atualizações de status de comando via SignalR para o grupo do dispositivo.
/// </summary>
public sealed class CommandHubPublisher : ICommandHubPublisher
{
    private readonly IHubContext<PositionHub> _hub;

    public CommandHubPublisher(IHubContext<PositionHub> hub)
    {
        _hub = hub;
    }

    public Task PublishAsync(string deviceId, int commandId, int statusId)
    {
        var group = PositionHub.GroupName(deviceId);
        return _hub.Clients.Group(group).SendAsync("AtualizacaoComando", new
        {
            commandId,
            deviceId,
            statusId,
            situacao = MapStatusText(statusId)
        });
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

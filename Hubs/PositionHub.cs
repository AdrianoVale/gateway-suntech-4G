using GatewaySunteh4G_NET8.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GatewaySunteh4G_NET8.Hubs;

[Authorize]
public sealed class PositionHub : Hub
{
    private readonly IPermissionQueryService _permissions;
    private readonly ILogger<PositionHub> _logger;

    public PositionHub(IPermissionQueryService permissions, ILogger<PositionHub> logger)
    {
        _permissions = permissions;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId   = Context.User?.FindFirst("sub")?.Value;
        var clientId = Context.User?.FindFirst("cid")?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(clientId))
        {
            _logger.LogWarning("Hub: conexão recusada — claims sub/cid ausentes. conn={ConnId}", Context.ConnectionId);
            Context.Abort();
            return;
        }

        var deviceIds = await _permissions.GetAuthorizedDeviceIdsAsync(userId, clientId);
        foreach (var deviceId in deviceIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(deviceId));
        }

        _logger.LogInformation(
            "Hub: conectado userId={UserId} clientId={ClientId} veiculos={Count} conn={ConnId}",
            userId, clientId, deviceIds.Count, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Hub: desconectado conn={ConnId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(string deviceId) => $"dev_{deviceId}";
}

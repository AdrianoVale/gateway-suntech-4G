using GatewaySunteh4G_NET8.Hubs;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.AspNetCore.SignalR;

namespace GatewaySunteh4G_NET8.Services;

public sealed class PositionHubPublisher : IPositionHubPublisher
{
    private readonly IHubContext<PositionHub> _hubContext;
    private readonly ILogger<PositionHubPublisher> _logger;

    public PositionHubPublisher(IHubContext<PositionHub> hubContext, ILogger<PositionHubPublisher> logger)
    {
        _hubContext = hubContext;
        _logger     = logger;
    }

    public async Task PublishAsync(string deviceId, PositionRecord position)
    {
        try
        {
            var group = PositionHub.GroupName(deviceId);
            await _hubContext.Clients.Group(group).SendAsync("NovaPos", new
            {
                deviceId,
                lat    = position.Latitude,
                lon    = position.Longitude,
                speed  = position.Speed,
                degree = position.Degree,
                gps    = position.Gps,
                ign    = position.Ign,
                block  = position.Block,
                dt     = position.DatetimeUtc.ToUnixTimeSeconds()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub: falha ao publicar posição para device {DeviceId}", deviceId);
        }
    }
}

/// <summary>
/// Implementação nula: usada quando Hub.Enabled = false.
/// </summary>
public sealed class NullPositionHubPublisher : IPositionHubPublisher
{
    public Task PublishAsync(string deviceId, PositionRecord position) => Task.CompletedTask;
}

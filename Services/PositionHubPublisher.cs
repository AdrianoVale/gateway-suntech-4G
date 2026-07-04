using GatewaySunteh4G_NET8.Hubs;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Globalization;

namespace GatewaySunteh4G_NET8.Services;

public sealed class PositionHubPublisher : IPositionHubPublisher
{
    private const double MinBatMainFallback = 0.01d;
    private static readonly ConcurrentDictionary<string, double> LastValidBatMainByDevice = new();

    private readonly IHubContext<PositionHub> _hubContext;
    private readonly ILogger<PositionHubPublisher> _logger;

    public PositionHubPublisher(IHubContext<PositionHub> hubContext, ILogger<PositionHubPublisher> logger)
    {
        _hubContext = hubContext;
        _logger     = logger;
    }

    public async Task PublishAsync(string deviceId, PositionRecord position)
    {
        var group = string.Empty;
        try
        {
            // Normaliza para coincidir com CAST(equipamento AS text) do banco (sem zeros à esquerda)
            if (long.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numId))
                deviceId = numId.ToString(CultureInfo.InvariantCulture);

            var batMainToSend = ResolveBatMain(deviceId, position.BatMain, position.BatBack);

            group = PositionHub.GroupName(deviceId);
            _logger.LogDebug(
                "Hub: enviando evento NovaPos para grupo {Group}. deviceId={DeviceId}, lat={Lat}, lon={Lon}, speed={Speed}, degree={Degree}, gps={Gps}, ign={Ign}, block={Block}, batMainOriginal={BatMainOriginal}, batMainEnviado={BatMainEnviado}, dtUnix={DtUnix}",
                group,
                deviceId,
                position.Latitude,
                position.Longitude,
                position.Speed,
                position.Degree,
                position.Gps,
                position.Ign,
                position.Block,
                position.BatMain,
                batMainToSend,
                position.DatetimeUtc.ToUnixTimeSeconds());

            await _hubContext.Clients.Group(group).SendAsync("NovaPos", new
            {
                deviceId,
                lat     = position.Latitude,
                lon     = position.Longitude,
                speed   = position.Speed,
                degree  = position.Degree,
                gps     = position.Gps,
                ign     = position.Ign,
                block   = position.Block,
                batMain = batMainToSend,
                dt      = position.DatetimeUtc.ToUnixTimeSeconds()
            });
            _logger.LogDebug(
                "Hub: evento NovaPos enviado com sucesso para grupo {Group}. deviceId={DeviceId}, lat={Lat}, lon={Lon}, speed={Speed}, degree={Degree}, gps={Gps}, ign={Ign}, block={Block}, batMainOriginal={BatMainOriginal}, batMainEnviado={BatMainEnviado}, dtUnix={DtUnix}",
                group,
                deviceId,
                position.Latitude,
                position.Longitude,
                position.Speed,
                position.Degree,
                position.Gps,
                position.Ign,
                position.Block,
                position.BatMain,
                batMainToSend,
                position.DatetimeUtc.ToUnixTimeSeconds());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Hub: falha ao enviar evento NovaPos para grupo {Group}. deviceId={DeviceId}, lat={Lat}, lon={Lon}, speed={Speed}, degree={Degree}, gps={Gps}, ign={Ign}, block={Block}, batMainOriginal={BatMainOriginal}, dtUnix={DtUnix}, dt={DatetimeUtc}",
                group,
                deviceId,
                position.Latitude,
                position.Longitude,
                position.Speed,
                position.Degree,
                position.Gps,
                position.Ign,
                position.Block,
                position.BatMain,
                position.DatetimeUtc.ToUnixTimeSeconds(),
                position.DatetimeUtc);
        }
    }

    private double ResolveBatMain(string deviceId, double batMain, double batBack)
    {
        if (batMain > 0)
        {
            LastValidBatMainByDevice[deviceId] = batMain;
            return batMain;
        }

        if (LastValidBatMainByDevice.TryGetValue(deviceId, out var lastValid) && lastValid > 0)
        {
            _logger.LogDebug(
                "Hub: batMain=0 para device {DeviceId}. Reutilizando último batMain válido={BatMainValido}",
                deviceId,
                lastValid);
            return lastValid;
        }

        if (batBack > 0)
        {
            LastValidBatMainByDevice[deviceId] = batBack;
            _logger.LogWarning(
                "Hub: batMain=0 sem histórico para device {DeviceId}. Usando batBack={BatBack} como fallback",
                deviceId,
                batBack);
            return batBack;
        }

        LastValidBatMainByDevice[deviceId] = MinBatMainFallback;
        _logger.LogWarning(
            "Hub: batMain=0 e sem fallback válido para device {DeviceId}. Usando fallback mínimo={Fallback}",
            deviceId,
            MinBatMainFallback);
        return MinBatMainFallback;
    }
}

/// <summary>
/// Implementação nula: usada quando Hub.Enabled = false.
/// </summary>
public sealed class NullPositionHubPublisher : IPositionHubPublisher
{
    public Task PublishAsync(string deviceId, PositionRecord position) => Task.CompletedTask;
}

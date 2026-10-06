using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;

namespace GatewaySunteh4G_NET8.Services;

/// <summary>
/// Implementação Redis do <see cref="INotifEventPublisher"/>. Publica no canal
/// Pub/Sub <c>blt:notif:events</c> (entrega imediata pelo WS server PHP) e no
/// stream durável <c>blt:notif:outbox</c>. Degradação graciosa: qualquer falha
/// de transporte é apenas logada (não quebra o processamento de telemetria).
/// </summary>
public sealed class RedisNotifEventPublisher : INotifEventPublisher
{
    // Códigos (MsgTypeId) que geram notificação ao cliente final.
    // Catálogo semântico em blt/docs/CENTRAL_NOTIFICACOES.md (seção 3).
    private static readonly HashSet<int> RelevantTypes = new()
    {
        2001, 2002, 2003, 4013, 4014, 4040, 4041, 4044, 4045
    };

    private const string Channel = "blt:notif:events";
    private const string Stream  = "blt:notif:outbox";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisNotifEventPublisher> _logger;

    public RedisNotifEventPublisher(IConnectionMultiplexer redis, ILogger<RedisNotifEventPublisher> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    public void PublishDeviceEvent(string deviceId, int msgTypeId)
    {
        if (!RelevantTypes.Contains(msgTypeId))
            return;

        // Fire-and-forget: falha na notificação não bloqueia o fluxo principal.
        _ = PublishAsync(deviceId, msgTypeId);
    }

    private async Task PublishAsync(string deviceId, int msgTypeId)
    {
        try
        {
            // Normaliza para coincidir com CAST(equipamento AS text) do banco (sem zeros à esquerda).
            if (long.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numId))
                deviceId = numId.ToString(CultureInfo.InvariantCulture);

            var payload = JsonSerializer.Serialize(new
            {
                code        = msgTypeId,
                device_id   = deviceId,
                origin      = "gateway",
                occurred_at = DateTimeOffset.UtcNow.ToString("O")
            });

            var db = _redis.GetDatabase();
            await db.PublishAsync(RedisChannel.Literal(Channel), payload).ConfigureAwait(false);
            await db.StreamAddAsync(Stream, "p", payload, maxLength: 50_000, useApproximateMaxLength: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao publicar evento de notificação (device {DeviceId}, msgType {MsgTypeId}).", deviceId, msgTypeId);
        }
    }
}

/// <summary>Versão no-op usada quando a Central de Notificações está desativada.</summary>
public sealed class NullNotifEventPublisher : INotifEventPublisher
{
    public void PublishDeviceEvent(string deviceId, int msgTypeId) { }
}

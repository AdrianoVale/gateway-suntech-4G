namespace GatewaySunteh4G_NET8.Services;

/// <summary>
/// Publica eventos de telemetria relevantes para a Central de Notificações BLT
/// no barramento Redis (canal <c>blt:notif:events</c>). Aditivo: não interfere
/// no fluxo de posição/comando já validado.
/// </summary>
public interface INotifEventPublisher
{
    /// <summary>
    /// Dispara (fire-and-forget) a publicação quando <paramref name="msgTypeId"/>
    /// é um dos códigos que geram notificação ao cliente final. Códigos fora do
    /// catálogo são ignorados internamente.
    /// </summary>
    void PublishDeviceEvent(string deviceId, int msgTypeId);
}

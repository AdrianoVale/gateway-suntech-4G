using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IGatewayPacketProcessor
{
    Task ProcessAsync(UdpEnvelope envelope, CancellationToken cancellationToken);
}
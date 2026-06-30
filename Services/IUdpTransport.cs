using System.Net;
using System.Net.Sockets;

namespace GatewaySunteh4G_NET8.Services;

public interface IUdpTransport
{
    void Attach(UdpClient client);
    Task SendAsync(byte[] payload, IPEndPoint remoteEndPoint, CancellationToken cancellationToken);
}
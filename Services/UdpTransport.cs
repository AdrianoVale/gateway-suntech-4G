using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace GatewaySunteh4G_NET8.Services;

public sealed class UdpTransport : IUdpTransport
{
    private readonly object _sync = new();
    private readonly ILogger<UdpTransport> _logger;
    private UdpClient? _client;

    public UdpTransport(ILogger<UdpTransport> logger)
    {
        _logger = logger;
    }

    public void Attach(UdpClient client)
    {
        lock (_sync)
        {
            _client = client;
        }
    }

    public async Task SendAsync(byte[] payload, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        UdpClient client;
        lock (_sync)
        {
            client = _client ?? throw new InvalidOperationException("Socket UDP ainda não foi inicializado.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendAsync(payload, payload.Length, remoteEndPoint);
        _logger.LogDebug("Pacote UDP enviado para {RemoteEndPoint} com {Bytes} bytes", remoteEndPoint, payload.Length);
    }
}
using System.Net;

namespace GatewaySunteh4G_NET8.Services.Models;

public sealed record UdpEnvelope(byte[] Payload, IPEndPoint RemoteEndPoint, DateTimeOffset ReceivedAtUtc);
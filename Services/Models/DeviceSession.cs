using System.Net;

namespace GatewaySunteh4G_NET8.Services.Models;

public sealed class DeviceSession
{
    public required string DeviceId { get; init; }

    public required string Header { get; init; }

    public required string Model { get; init; }

    public required IPEndPoint RemoteEndPoint { get; init; }

    public required DateTimeOffset LastSeenUtc { get; init; }

    public DateTimeOffset? DeviceTimestampUtc { get; init; }

    public string? Latitude { get; init; }

    public string? Longitude { get; init; }

    public string? RawMessage { get; init; }

    public byte[]? LastMessageBytes { get; init; }
}
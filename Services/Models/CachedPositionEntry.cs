namespace GatewaySunteh4G_NET8.Services.Models;

public sealed class CachedPositionEntry
{
    public required PositionRecord Position { get; init; }
    public required string? Plate { get; init; }
    public required FileInfo File { get; init; }
}
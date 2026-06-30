namespace GatewaySunteh4G_NET8.Services.Models;

public sealed class PendingCommand
{
    public required CommandRecord Command { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; set; }
}
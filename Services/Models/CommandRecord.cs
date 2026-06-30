namespace GatewaySunteh4G_NET8.Services.Models;

public sealed class CommandRecord
{
    public required int Id { get; init; }
    public required string DeviceId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public required string Parameters { get; set; }
    public required int CommandTypeId { get; init; }
    public required int StatusCommandId { get; set; }
}
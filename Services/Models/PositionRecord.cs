namespace GatewaySunteh4G_NET8.Services.Models;

public sealed class PositionRecord
{
    public required string DeviceId { get; init; }
    public required DateTimeOffset DatetimeUtc { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required int Speed { get; init; }
    public required double Degree { get; init; }
    public required bool Gps { get; init; }
    public required int Sat { get; init; }
    public required bool Ign { get; init; }
    public required bool Block { get; init; }
    public required string Io { get; init; }
    public required double BatMain { get; init; }
    public required double BatBack { get; init; }
    public required bool Storage { get; init; }
    public required int MsgTypeId { get; init; }
    public required int DeviceModelId { get; init; }
}
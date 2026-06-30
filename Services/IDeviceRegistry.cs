using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IDeviceRegistry
{
    void Upsert(DeviceSession session);
    bool TryGet(string deviceId, out DeviceSession? session);
    int RemoveInactive(TimeSpan maxAge, DateTimeOffset nowUtc);
    int Count { get; }
}
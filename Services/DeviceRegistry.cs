using System.Collections.Concurrent;
using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceSession> _devices = new(StringComparer.Ordinal);

    public int Count => _devices.Count;

    public void Upsert(DeviceSession session)
    {
        _devices.AddOrUpdate(session.DeviceId, session, (_, _) => session);
    }

    public bool TryGet(string deviceId, out DeviceSession? session)
    {
        return _devices.TryGetValue(deviceId, out session);
    }

    public int RemoveInactive(TimeSpan maxAge, DateTimeOffset nowUtc)
    {
        var removed = 0;
        foreach (var pair in _devices)
        {
            if (nowUtc - pair.Value.LastSeenUtc < maxAge)
            {
                continue;
            }

            if (_devices.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }
}
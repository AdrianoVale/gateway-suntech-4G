using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface ICommandRegistry
{
    void Upsert(PendingCommand command);
    bool TryGet(string deviceId, out PendingCommand? command);
    PendingCommand? Complete(string deviceId);
    int RemoveInactive(TimeSpan maxAge, DateTimeOffset nowUtc);
    int Count { get; }
}
using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IPositionPersistenceService
{
    void PersistOrCache(PositionRecord position);
    void ReplayPending();
    int PendingCacheCount();
}
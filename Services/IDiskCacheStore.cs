using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IDiskCacheStore
{
    void Save(PositionRecord position, string? plate);
    IReadOnlyList<CachedPositionEntry> LoadPending();
    void Remove(FileInfo file);
    int PendingCount();
}
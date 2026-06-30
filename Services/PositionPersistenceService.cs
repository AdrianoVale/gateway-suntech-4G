using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public sealed class PositionPersistenceService : IPositionPersistenceService
{
    private readonly ILogger<PositionPersistenceService> _logger;
    private readonly IGatewayDataService _dataService;
    private readonly IDiskCacheStore _diskCacheStore;
    private readonly IGatewayMetrics _metrics;

    public PositionPersistenceService(
        ILogger<PositionPersistenceService> logger,
        IGatewayDataService dataService,
        IDiskCacheStore diskCacheStore,
        IGatewayMetrics metrics)
    {
        _logger = logger;
        _dataService = dataService;
        _diskCacheStore = diskCacheStore;
        _metrics = metrics;
    }

    public void PersistOrCache(PositionRecord position)
    {
        if (_dataService.InsertPosition(position))
        {
            _metrics.IncrementPositionsInserted();
            TryInsertVehicleTable(position, _dataService.GetVehiclePlateByDeviceId(position.DeviceId));
            _metrics.SetCachePendingFiles(_diskCacheStore.PendingCount());
            return;
        }

        _diskCacheStore.Save(position, null);
        _metrics.IncrementPositionsCacheSaved();
        _metrics.SetCachePendingFiles(_diskCacheStore.PendingCount());
    }

    public void ReplayPending()
    {
        var entries = _diskCacheStore.LoadPending();
        if (entries.Count == 0)
        {
            _metrics.SetCachePendingFiles(0);
            return;
        }

        foreach (var entry in entries)
        {
            if (_dataService.InsertPosition(entry.Position))
            {
                var plate = entry.Plate ?? _dataService.GetVehiclePlateByDeviceId(entry.Position.DeviceId);
                TryInsertVehicleTable(entry.Position, plate);
                _diskCacheStore.Remove(entry.File);
                _metrics.IncrementReplaySuccess();
                continue;
            }

            _metrics.IncrementReplayFailure();
            _logger.LogWarning("Replay do cache interrompido em {FileName}; item mantido para nova tentativa", entry.File.Name);
            break;
        }

        _metrics.SetCachePendingFiles(_diskCacheStore.PendingCount());
    }

    public int PendingCacheCount()
    {
        var count = _diskCacheStore.PendingCount();
        _metrics.SetCachePendingFiles(count);
        return count;
    }

    private void TryInsertVehicleTable(PositionRecord position, string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return;
        }

        _dataService.EnsureVehicleTableAndInsert(position, plate);
    }

}
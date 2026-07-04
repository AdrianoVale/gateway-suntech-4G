using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IPositionPersistenceService
{
    void PersistOrCache(PositionRecord position);
    void ReplayPending();
    int PendingCacheCount();
    /// <summary>
    /// Busca no banco a última leitura de bateria considerada válida para o device.
    /// bat_main entre 10–25 V e bat_back entre 2–6 V.
    /// </summary>
    (double BatMain, double BatBack)? GetLastValidBattery(string deviceId);
}
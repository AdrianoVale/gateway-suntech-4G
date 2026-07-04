using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IGatewayDataService
{
    bool InsertPosition(PositionRecord position);
    long GetTotalPositionCount();
    void CleanupPositions();
    string? GetVehiclePlateByDeviceId(string deviceId);
    bool EnsureVehicleTableAndInsert(PositionRecord position, string plate);
    /// <summary>
    /// Retorna a última leitura válida de bateria para o device, conforme faixas definidas.
    /// bat_main: 10–25 V (sistemas 12V e 24V), bat_back: 2–6 V (backup Li-ion).
    /// </summary>
    (double BatMain, double BatBack)? GetLastValidBattery(string deviceId);
    IReadOnlyList<CommandRecord> GetCommands(bool includeStatus3);
    IReadOnlyList<CommandRecord> GetCommandsByDeviceId(string deviceId, bool includeStatus3);
    bool UpdateCommand(CommandRecord command);
    bool UpdateCommandStatusIfCurrent(CommandRecord command, int currentStatus, int nextStatus, DateTimeOffset updatedAtUtc);
}
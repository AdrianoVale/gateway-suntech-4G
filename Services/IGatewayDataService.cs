using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IGatewayDataService
{
    bool InsertPosition(PositionRecord position);
    long GetTotalPositionCount();
    void CleanupPositions();
    string? GetVehiclePlateByDeviceId(string deviceId);
    bool EnsureVehicleTableAndInsert(PositionRecord position, string plate);
    IReadOnlyList<CommandRecord> GetCommands(bool includeStatus3);
    IReadOnlyList<CommandRecord> GetCommandsByDeviceId(string deviceId, bool includeStatus3);
    bool UpdateCommand(CommandRecord command);
    bool UpdateCommandStatusIfCurrent(CommandRecord command, int currentStatus, int nextStatus, DateTimeOffset updatedAtUtc);
}
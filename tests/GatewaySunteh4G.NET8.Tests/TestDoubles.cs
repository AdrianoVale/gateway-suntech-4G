using System.Net;
using System.Net.Sockets;
using GatewaySunteh4G_NET8.Services;
using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G.NET8.Tests;

internal sealed class FakeGatewayDataService : IGatewayDataService
{
    public List<CommandRecord> UpdatedCommands { get; } = new();

    public bool InsertPosition(PositionRecord position) => true;

    public long GetTotalPositionCount() => 0;

    public void CleanupPositions()
    {
    }

    public string? GetVehiclePlateByDeviceId(string deviceId) => null;

    public bool EnsureVehicleTableAndInsert(PositionRecord position, string plate) => true;

    public IReadOnlyList<CommandRecord> GetCommands(bool includeStatus3) => Array.Empty<CommandRecord>();

    public IReadOnlyList<CommandRecord> GetCommandsByDeviceId(string deviceId, bool includeStatus3) => Array.Empty<CommandRecord>();

    public bool UpdateCommand(CommandRecord command)
    {
        UpdatedCommands.Add(command);
        return true;
    }

    public bool UpdateCommandStatusIfCurrent(CommandRecord command, int currentStatus, int nextStatus, DateTimeOffset updatedAtUtc)
    {
        if (command.StatusCommandId != currentStatus)
        {
            return false;
        }

        command.StatusCommandId = nextStatus;
        command.UpdatedAtUtc = updatedAtUtc;
        return true;
    }
}

internal sealed class FakeUdpTransport : IUdpTransport
{
    public void Attach(UdpClient client)
    {
    }

    public Task SendAsync(byte[] payload, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed class FakeGatewayMetrics : IGatewayMetrics
{
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    public long MessagesDecoded { get; private set; }
    public long DecodeErrors { get; private set; }

    public void IncrementPacketsReceived()
    {
    }

    public void IncrementMessagesDecoded() => MessagesDecoded++;

    public void IncrementDecodeErrors() => DecodeErrors++;

    public void IncrementCommandsSent()
    {
    }

    public void IncrementCommandsFailed()
    {
    }

    public void IncrementAckSent()
    {
    }

    public void IncrementAckFailed()
    {
    }

    public void IncrementReceiverDropped()
    {
    }

    public void IncrementReplaySuccess()
    {
    }

    public void IncrementReplayFailure()
    {
    }

    public void IncrementCommandsClaimFailed()
    {
    }

    public void IncrementPositionsInserted()
    {
    }

    public void IncrementPositionsCacheSaved()
    {
    }

    public void SetReceiverQueueDepth(int value)
    {
    }

    public void SetReceiverActiveWorkers(int value)
    {
    }

    public void SetReceiverConfiguredWorkers(int value)
    {
    }

    public void SetDevicesConnected(int value)
    {
    }

    public void SetCommandsPending(int value)
    {
    }

    public void SetCachePendingFiles(int value)
    {
    }

    public string RenderPrometheus() => string.Empty;
}

internal sealed class FakePositionPersistenceService : IPositionPersistenceService
{
    public List<PositionRecord> PersistedPositions { get; } = new();

    public void PersistOrCache(PositionRecord position)
    {
        PersistedPositions.Add(position);
    }

    public void ReplayPending()
    {
    }

    public int PendingCacheCount() => 0;
}

internal sealed class FakeCommandDispatcher : ICommandDispatcher
{
    public List<string> RetryCalls { get; } = new();
    public List<string> ConfirmCalls { get; } = new();

    public void PollAndDispatch(bool firstPoll)
    {
    }

    public void RetryPendingCommandForDevice(string deviceId)
    {
        RetryCalls.Add(deviceId);
    }

    public void ConfirmPendingCommandOnNewPosition(string deviceId)
    {
        ConfirmCalls.Add(deviceId);
    }

    public void HandleResponse(string deviceId, string commandCode1, string commandCode2, string? extraInfo = null)
    {
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

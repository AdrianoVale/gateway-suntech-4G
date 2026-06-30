namespace GatewaySunteh4G_NET8.Services;

public interface IGatewayMetrics
{
    DateTimeOffset StartedAtUtc { get; }

    void IncrementPacketsReceived();
    void IncrementMessagesDecoded();
    void IncrementDecodeErrors();
    void IncrementCommandsSent();
    void IncrementCommandsFailed();
    void IncrementAckSent();
    void IncrementAckFailed();
    void IncrementReceiverDropped();
    void IncrementReplaySuccess();
    void IncrementReplayFailure();
    void IncrementCommandsClaimFailed();
    void IncrementPositionsInserted();
    void IncrementPositionsCacheSaved();
    void SetReceiverQueueDepth(int value);
    void SetReceiverActiveWorkers(int value);
    void SetReceiverConfiguredWorkers(int value);
    void SetDevicesConnected(int value);
    void SetCommandsPending(int value);
    void SetCachePendingFiles(int value);
    string RenderPrometheus();
}
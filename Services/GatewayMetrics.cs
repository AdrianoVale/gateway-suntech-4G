using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using GatewaySunteh4G_NET8.Options;

namespace GatewaySunteh4G_NET8.Services;

public sealed class GatewayMetrics : IGatewayMetrics
{
    private readonly GatewayOptions _options;
    private long _packetsReceived;
    private long _messagesDecoded;
    private long _decodeErrors;
    private long _commandsSent;
    private long _commandsFailed;
    private long _ackSent;
    private long _ackFailed;
    private long _receiverDropped;
    private long _replaySuccess;
    private long _replayFailure;
    private long _commandsClaimFailed;
    private long _positionsInserted;
    private long _positionsCacheSaved;
    private long _receiverQueueDepth;
    private long _receiverActiveWorkers;
    private long _receiverConfiguredWorkers;
    private long _devicesConnected;
    private long _commandsPending;
    private long _cachePendingFiles;

    public GatewayMetrics(IOptions<GatewayOptions> options)
    {
        _options = options.Value;
        StartedAtUtc = DateTimeOffset.UtcNow;
        SetReceiverConfiguredWorkers(_options.Udp.WorkerCount);
    }

    public DateTimeOffset StartedAtUtc { get; }

    public void IncrementPacketsReceived() => Interlocked.Increment(ref _packetsReceived);
    public void IncrementMessagesDecoded() => Interlocked.Increment(ref _messagesDecoded);
    public void IncrementDecodeErrors() => Interlocked.Increment(ref _decodeErrors);
    public void IncrementCommandsSent() => Interlocked.Increment(ref _commandsSent);
    public void IncrementCommandsFailed() => Interlocked.Increment(ref _commandsFailed);
    public void IncrementAckSent() => Interlocked.Increment(ref _ackSent);
    public void IncrementAckFailed() => Interlocked.Increment(ref _ackFailed);
    public void IncrementReceiverDropped() => Interlocked.Increment(ref _receiverDropped);
    public void IncrementReplaySuccess() => Interlocked.Increment(ref _replaySuccess);
    public void IncrementReplayFailure() => Interlocked.Increment(ref _replayFailure);
    public void IncrementCommandsClaimFailed() => Interlocked.Increment(ref _commandsClaimFailed);
    public void IncrementPositionsInserted() => Interlocked.Increment(ref _positionsInserted);
    public void IncrementPositionsCacheSaved() => Interlocked.Increment(ref _positionsCacheSaved);
    public void SetReceiverQueueDepth(int value) => Interlocked.Exchange(ref _receiverQueueDepth, value);
    public void SetReceiverActiveWorkers(int value) => Interlocked.Exchange(ref _receiverActiveWorkers, value);
    public void SetReceiverConfiguredWorkers(int value) => Interlocked.Exchange(ref _receiverConfiguredWorkers, value);
    public void SetDevicesConnected(int value) => Interlocked.Exchange(ref _devicesConnected, value);
    public void SetCommandsPending(int value) => Interlocked.Exchange(ref _commandsPending, value);
    public void SetCachePendingFiles(int value) => Interlocked.Exchange(ref _cachePendingFiles, value);

    public string RenderPrometheus()
    {
        var labels = $"project=\"{_options.ProjectLabel}\"";
        var sb = new StringBuilder(4096);

        AppendCounter(sb, "gateway_packets_received_total", "Pacotes UDP recebidos no socket", labels, Interlocked.Read(ref _packetsReceived));
        AppendCounter(sb, "gateway_messages_decoded_total", "Mensagens 4G decodificadas", labels, Interlocked.Read(ref _messagesDecoded));
        AppendCounter(sb, "gateway_decode_errors_total", "Erros de decode", labels, Interlocked.Read(ref _decodeErrors));
        AppendCounter(sb, "gateway_commands_sent_total", "Comandos enviados ao rastreador", labels, Interlocked.Read(ref _commandsSent));
        AppendCounter(sb, "gateway_commands_failed_total", "Falhas no envio de comandos", labels, Interlocked.Read(ref _commandsFailed));
        AppendCounter(sb, "gateway_ack_sent_total", "ACKs enviados", labels, Interlocked.Read(ref _ackSent));
        AppendCounter(sb, "gateway_ack_failed_total", "Falhas no envio de ACK", labels, Interlocked.Read(ref _ackFailed));
        AppendCounter(sb, "gateway_receiver_dropped_total", "Pacotes descartados por fila cheia no receiver", labels, Interlocked.Read(ref _receiverDropped));
        AppendCounter(sb, "gateway_replay_success_total", "Entradas de replay concluídas com sucesso", labels, Interlocked.Read(ref _replaySuccess));
        AppendCounter(sb, "gateway_replay_failure_total", "Falhas no replay", labels, Interlocked.Read(ref _replayFailure));
        AppendCounter(sb, "gateway_commands_claim_failed_total", "Falhas de claim atômico de comando", labels, Interlocked.Read(ref _commandsClaimFailed));
        AppendCounter(sb, "gateway_positions_inserted_total", "Posições processadas com sucesso", labels, Interlocked.Read(ref _positionsInserted));
        AppendCounter(sb, "gateway_positions_cache_saved_total", "Posições encaminhadas para fallback de cache", labels, Interlocked.Read(ref _positionsCacheSaved));

        AppendGauge(sb, "gateway_receiver_queue_depth", "Tamanho atual da fila do receiver", labels, Interlocked.Read(ref _receiverQueueDepth));
        AppendGauge(sb, "gateway_receiver_active_threads", "Workers ativos no processamento UDP", labels, Interlocked.Read(ref _receiverActiveWorkers));
        AppendGauge(sb, "gateway_receiver_max_threads", "Workers configurados no receiver", labels, Interlocked.Read(ref _receiverConfiguredWorkers));
        AppendGauge(sb, "gateway_devices_connected", "Devices conectados em memória", labels, Interlocked.Read(ref _devicesConnected));
        AppendGauge(sb, "gateway_commands_pending", "Comandos pendentes em memória", labels, Interlocked.Read(ref _commandsPending));
        AppendGauge(sb, "gateway_cache_pending_files", "Arquivos pendentes no cache de disco", labels, Interlocked.Read(ref _cachePendingFiles));
        AppendGauge(sb, "gateway_process_uptime_seconds", "Uptime do processo em segundos", labels, (long)(DateTimeOffset.UtcNow - StartedAtUtc).TotalSeconds);
        AppendGauge(sb, "gateway_dotnet_gc_heap_size_bytes", "Heap gerenciado aproximado do processo", labels, GC.GetTotalMemory(false));
        AppendGauge(sb, "gateway_dotnet_working_set_bytes", "Working set do processo", labels, Environment.WorkingSet);
        AppendGauge(sb, "gateway_dotnet_threadpool_threads", "Threads atuais do pool", labels, ThreadPool.ThreadCount);

        return sb.ToString();
    }

    private static void AppendCounter(StringBuilder sb, string name, string help, string labels, long value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        sb.Append("# TYPE ").Append(name).Append(" counter\n");
        sb.Append(name).Append('{').Append(labels).Append("} ")
            .Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    private static void AppendGauge(StringBuilder sb, string name, string help, string labels, long value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        sb.Append("# TYPE ").Append(name).Append(" gauge\n");
        sb.Append(name).Append('{').Append(labels).Append("} ")
            .Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }
}
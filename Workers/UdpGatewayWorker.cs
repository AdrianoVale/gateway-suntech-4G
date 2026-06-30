using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Workers;

public sealed class UdpGatewayWorker : BackgroundService
{
    private readonly ILogger<UdpGatewayWorker> _logger;
    private readonly IGatewayMetrics _metrics;
    private readonly IGatewayPacketProcessor _processor;
    private readonly IUdpTransport _transport;
    private readonly GatewayOptions _options;
    private readonly Channel<UdpEnvelope> _channel;
    private int _activeWorkers;
    private int _queuedItems;
    private UdpClient? _client;

    public UdpGatewayWorker(
        ILogger<UdpGatewayWorker> logger,
        IGatewayMetrics metrics,
        IGatewayPacketProcessor processor,
        IUdpTransport transport,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _metrics = metrics;
        _processor = processor;
        _transport = transport;
        _options = options.Value;
        _channel = Channel.CreateBounded<UdpEnvelope>(new BoundedChannelOptions(_options.Udp.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = true
        });
        _metrics.SetReceiverConfiguredWorkers(_options.Udp.WorkerCount);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerTasks = Enumerable.Range(0, _options.Udp.WorkerCount)
            .Select(index => Task.Run(() => ProcessQueueAsync(index + 1, stoppingToken), stoppingToken))
            .ToArray();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var client = new UdpClient(_options.Udp.Port);
                client.Client.ReceiveBufferSize = _options.Udp.ReceiveBufferBytes;
                client.Client.SendBufferSize = _options.Udp.ReceiveBufferBytes;
                _client = client;
                _transport.Attach(client);

                _logger.LogInformation(
                    "Receiver UDP iniciado na porta {Port} com buffer {BufferBytes} e {Workers} workers",
                    _options.Udp.Port,
                    _options.Udp.ReceiveBufferBytes,
                    _options.Udp.WorkerCount);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync(stoppingToken);
                    _metrics.IncrementPacketsReceived();

                    var payload = result.Buffer.Length == 0
                        ? Array.Empty<byte>()
                        : result.Buffer.ToArray();

                    var envelope = new UdpEnvelope(payload, result.RemoteEndPoint, DateTimeOffset.UtcNow);
                    if (_channel.Writer.TryWrite(envelope))
                    {
                        var queued = Interlocked.Increment(ref _queuedItems);
                        _metrics.SetReceiverQueueDepth(queued);
                        continue;
                    }

                    _metrics.IncrementReceiverDropped();
                    _logger.LogWarning("Pacote descartado de {RemoteEndPoint}: fila cheia ({QueueCapacity})", result.RemoteEndPoint, _options.Udp.QueueCapacity);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no receiver UDP. Nova tentativa em {BackoffMilliseconds} ms", _options.Udp.ReceiverBackoffMilliseconds);
                await Task.Delay(_options.Udp.ReceiverBackoffMilliseconds, stoppingToken);
            }
        }

        _channel.Writer.TryComplete();
        await Task.WhenAll(workerTasks);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Encerrando receiver UDP com graceful shutdown...");
        _client?.Dispose();
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessQueueAsync(int workerId, CancellationToken stoppingToken)
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            Interlocked.Decrement(ref _queuedItems);
            _metrics.SetReceiverQueueDepth(Math.Max(0, Volatile.Read(ref _queuedItems)));

            var active = Interlocked.Increment(ref _activeWorkers);
            _metrics.SetReceiverActiveWorkers(active);
            try
            {
                await _processor.ProcessAsync(envelope, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogError(ex, "Worker UDP {WorkerId} falhou ao processar pacote de {RemoteEndPoint}", workerId, envelope.RemoteEndPoint);
            }
            finally
            {
                active = Interlocked.Decrement(ref _activeWorkers);
                _metrics.SetReceiverActiveWorkers(Math.Max(0, active));
            }
        }
    }
}
using System.Net;
using System.Text;
using GatewaySunteh4G_NET8.Services;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GatewaySunteh4G.NET8.Tests;

public sealed class St4315PacketProcessorTests
{
    private static St4315PacketProcessor BuildProcessor(
        FakeGatewayMetrics metrics,
        DeviceRegistry deviceRegistry,
        CommandRegistry commandRegistry,
        FakePositionPersistenceService persistence,
        FakeCommandDispatcher dispatcher)
    {
        return new St4315PacketProcessor(
            NullLogger<St4315PacketProcessor>.Instance,
            metrics,
            deviceRegistry,
            commandRegistry,
            persistence,
            dispatcher,
            new NullPositionHubPublisher());
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptQuotedAlvPacket_AndUpdateDeviceRegistry()
    {
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var payload = Encoding.ASCII.GetBytes("\"ALV;2290032094\"");
        var envelope = new UdpEnvelope(
            payload,
            new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12345),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        Assert.True(deviceRegistry.TryGet("2290032094", out var session));
        Assert.NotNull(session);
        Assert.Equal("ALV", session.Header);
        var retriedDevice = Assert.Single(dispatcher.RetryCalls);
        Assert.Equal("2290032094", retriedDevice);
        Assert.Equal(1, metrics.MessagesDecoded);
        Assert.Equal(0, metrics.DecodeErrors);
    }

    [Fact]
    public async Task ProcessAsync_ShouldParseUexPacket_AndPersistPosition()
    {
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var uex = "UEX;0360000001;3FFFFF;36;1.0.14;1;20161117;08:37:39;0000004F;450;0;0014;20;+37.479323;+126.887827;62.03;65.43;10;1;00000101;00001000;25; Welcome to ST SUNLAB World!;12;0; 4759;78245;13.5";
        var envelope = new UdpEnvelope(
            Encoding.ASCII.GetBytes(uex),
            new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12345),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        Assert.True(deviceRegistry.TryGet("0360000001", out var session));
        Assert.NotNull(session);
        Assert.Equal("UEX", session.Header);

        var position = Assert.Single(persistence.PersistedPositions);
        Assert.Equal("0360000001", position.DeviceId);
        Assert.Equal(37.479323, position.Latitude, 6);
        Assert.Equal(126.887827, position.Longitude, 6);
        Assert.Equal(62, position.Speed);
        Assert.Equal(10, position.Sat);
        Assert.True(position.Gps);
        Assert.Equal(13.5, position.BatMain, 6);
        Assert.Equal(1, metrics.MessagesDecoded);
        Assert.Equal(0, metrics.DecodeErrors);
    }

    [Fact]
    public async Task ProcessAsync_ShouldParseTrvPacket_AndPersistTripFinishPosition()
    {
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var trv = "TRV;0360000001;07FFFFF;36;1.0.14;1;20161117;08:37:39;+37.479323;+126.887827;+38.479323;+127.887827;500000193E0CCD01;23824;10800;436;2;;325;3;102.59;38.29; 78245; 319;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0";
        var envelope = new UdpEnvelope(
            Encoding.ASCII.GetBytes(trv),
            new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12345),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        Assert.True(deviceRegistry.TryGet("0360000001", out var session));
        Assert.NotNull(session);
        Assert.Equal("TRV", session.Header);
        Assert.Equal("+38.479323", session.Latitude);
        Assert.Equal("+127.887827", session.Longitude);

        var position = Assert.Single(persistence.PersistedPositions);
        Assert.Equal("0360000001", position.DeviceId);
        Assert.Equal(38.479323, position.Latitude, 6);
        Assert.Equal(127.887827, position.Longitude, 6);
        Assert.Equal(38, position.Speed);
        Assert.Equal(0, position.BatMain);
        Assert.Equal(1, metrics.MessagesDecoded);
        Assert.Equal(0, metrics.DecodeErrors);
    }

    // -----------------------------------------------------------------
    // Testes do fluxo de confirmação de comandos via STT/ALT
    // -----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SttPacket_ShouldCallConfirmPendingCommand_NotRetry()
    {
        // Garante que STT chama ConfirmPendingCommandOnNewPosition (e não RetryPendingCommandForDevice)
        // — esse é o fix do bug de produção onde status 3 ficava definitivo.
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var stt = "STT;123456789;07FFFFF;36;1.0.14;0;20250525;12:00:00;0000004F;0;0;0;20;-23.550520;-46.633308;0;0;8;1;00000001;00000000;0;0;0;12.6";
        var envelope = new UdpEnvelope(
            Encoding.ASCII.GetBytes(stt),
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 9999),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        // Deve chamar Confirm (não Retry) para dispositivos com STT
        var confirmedDevice = Assert.Single(dispatcher.ConfirmCalls);
        Assert.Equal("123456789", confirmedDevice);
        Assert.Empty(dispatcher.RetryCalls);
        var position = Assert.Single(persistence.PersistedPositions);
        Assert.Equal(12.6, position.BatMain, 6);
        Assert.Equal(1, metrics.MessagesDecoded);
    }

    [Fact]
    public async Task ProcessAsync_AltPacket_ShouldCallConfirmPendingCommand_NotRetry()
    {
        // Pacote ALT (alerta) também deve confirmar comandos pendentes via STT/ALT.
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var alt = "ALT;123456789;07FFFFF;36;1.0.14;0;20250525;12:00:00;0000004F;0;0;0;20;-23.550520;-46.633308;0;0;8;1;00000001;00000000;0;0;0;12.6";
        var envelope = new UdpEnvelope(
            Encoding.ASCII.GetBytes(alt),
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 9999),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        var confirmedDevice = Assert.Single(dispatcher.ConfirmCalls);
        Assert.Equal("123456789", confirmedDevice);
        Assert.Empty(dispatcher.RetryCalls);
        Assert.Equal(1, metrics.MessagesDecoded);
    }

    [Fact]
    public async Task ProcessAsync_AltPacket_Extended31Fields_ShouldParseBatteryAtIndex24And25()
    {
        // Pacote ALT estendido com 31 campos onde bat_main está no índice 24 e bat_back no índice 25,
        // seguidos de campos adicionais sem hex_footer fixo no fim.
        // Formato: ...;9;1;;13.97;4.2;;10;172315;0;470
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var alt = "ALT;2290032090;3FFFFF;229;1.0.13;1;20260721;20:31:58;0913660D;724;2;98C9;56;-0.833722;-46.602755;0.00;0.00;21;1;00000000;00000000;9;1;;13.97;4.2;;10;172315;0;470";
        var envelope = new UdpEnvelope(
            Encoding.ASCII.GetBytes(alt),
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 9999),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        Assert.True(deviceRegistry.TryGet("2290032090", out var session));
        Assert.NotNull(session);
        Assert.Equal("ALT", session.Header);

        var position = Assert.Single(persistence.PersistedPositions);
        Assert.Equal("2290032090", position.DeviceId);
        Assert.Equal(-0.833722, position.Latitude, 6);
        Assert.Equal(-46.602755, position.Longitude, 6);
        Assert.Equal(13.97, position.BatMain, 6);
        Assert.Equal(4.2, position.BatBack, 6);
        Assert.Equal(1, metrics.MessagesDecoded);
        Assert.Equal(0, metrics.DecodeErrors);
    }

    [Fact]
    public async Task ProcessAsync_SttPacket_Extended33Fields_ShouldParseBatteryAtIndex26And27()
    {
        // Pacote STT estendido com 33 campos onde bat_main está no índice 26 e bat_back no índice 27,
        // seguidos de campos adicionais (sem hex_footer fixo no fim).
        // Formato: ...;0003800F;13.29;4.0;;10;5916;5916;43
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var stt = "STT;2290032096;FFFFFF;229;1.0.13;1;20260721;20:09:05;0918EBD4;724;2;98F4;59;-1.353393;-48.410495;23.26;358.08;16;1;00000001;00000000;1;1;0068;;0003800F;13.29;4.0;;10;5916;5916;43";
        var envelope = new UdpEnvelope(
            Encoding.ASCII.GetBytes(stt),
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 9999),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        Assert.True(deviceRegistry.TryGet("2290032096", out var session));
        Assert.NotNull(session);
        Assert.Equal("STT", session.Header);

        var position = Assert.Single(persistence.PersistedPositions);
        Assert.Equal("2290032096", position.DeviceId);
        Assert.Equal(-1.353393, position.Latitude, 6);
        Assert.Equal(-48.410495, position.Longitude, 6);
        Assert.Equal(13.29, position.BatMain, 6);
        Assert.Equal(4.0, position.BatBack, 6);
        Assert.Equal(1, metrics.MessagesDecoded);
        Assert.Equal(0, metrics.DecodeErrors);
    }

    [Fact]
    public async Task ProcessAsync_AlvPacket_ShouldCallRetry_NotConfirm()
    {
        // ALV (heartbeat) não carrega posição real — deve continuar usando Retry, não Confirm.
        var metrics = new FakeGatewayMetrics();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var persistence = new FakePositionPersistenceService();
        var dispatcher = new FakeCommandDispatcher();

        var processor = BuildProcessor(metrics, deviceRegistry, commandRegistry, persistence, dispatcher);

        var payload = Encoding.ASCII.GetBytes("ALV;123456789");
        var envelope = new UdpEnvelope(
            payload,
            new IPEndPoint(IPAddress.Parse("10.0.0.1"), 9999),
            DateTimeOffset.UtcNow);

        await processor.ProcessAsync(envelope, CancellationToken.None);

        var retriedDevice = Assert.Single(dispatcher.RetryCalls);
        Assert.Equal("123456789", retriedDevice);
        Assert.Empty(dispatcher.ConfirmCalls);
    }
}

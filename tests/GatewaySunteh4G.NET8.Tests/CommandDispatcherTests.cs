using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GatewaySunteh4G.NET8.Tests;

public sealed class CommandDispatcherTests
{
    private static CommandDispatcher BuildDispatcher(
        FakeGatewayDataService dataService,
        DeviceRegistry deviceRegistry,
        CommandRegistry commandRegistry,
        FakeGatewayMetrics? metrics = null,
        TimeProvider? timeProvider = null)
    {
        return new CommandDispatcher(
            NullLogger<CommandDispatcher>.Instance,
            dataService,
            deviceRegistry,
            commandRegistry,
            new FakeUdpTransport(),
            metrics ?? new FakeGatewayMetrics(),
            timeProvider ?? TimeProvider.System,
            Options.Create(new GatewayOptions()),
            postgresDataService: null);
    }

    [Fact]
    public void HandleResponse_ShouldUpdateStatusTo2_ForMatchedResponseWithDeviceIdAndCodeVariations()
    {
        var dataService = new FakeGatewayDataService();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();

        var dispatcher = BuildDispatcher(dataService, deviceRegistry, commandRegistry);

        var command = new CommandRecord
        {
            Id = 10,
            DeviceId = "290032094",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = null,
            Parameters = string.Empty,
            CommandTypeId = 1,
            StatusCommandId = 3
        };

        commandRegistry.Upsert(new PendingCommand
        {
            Command = command,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        dispatcher.HandleResponse("0290032094", "4", "1", "OK");

        Assert.Equal(2, command.StatusCommandId);
        Assert.Single(dataService.UpdatedCommands);
        Assert.Equal(10, dataService.UpdatedCommands[0].Id);
    }

    [Fact]
    public void HandleResponse_ShouldUpdateStatusTo4_ForMismatchedCodes()
    {
        var dataService = new FakeGatewayDataService();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();

        var dispatcher = BuildDispatcher(dataService, deviceRegistry, commandRegistry);

        var command = new CommandRecord
        {
            Id = 11,
            DeviceId = "2290032094",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = null,
            Parameters = string.Empty,
            CommandTypeId = 1,
            StatusCommandId = 3
        };

        commandRegistry.Upsert(new PendingCommand
        {
            Command = command,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        dispatcher.HandleResponse("2290032094", "04", "02", "OK");

        Assert.Equal(4, command.StatusCommandId);
        Assert.Single(dataService.UpdatedCommands);
        Assert.Equal(11, dataService.UpdatedCommands[0].Id);
    }

    // -----------------------------------------------------------------
    // ConfirmPendingCommandOnNewPosition
    // -----------------------------------------------------------------

    [Fact]
    public void ConfirmPendingCommandOnNewPosition_WhenStatus3_ShouldUpdateStatusTo2()
    {
        // Reproduz o bug de produção: comando fica em status 3 porque o RES
        // UDP foi perdido, mas o dispositivo enviou nova posição STT.
        var dataService = new FakeGatewayDataService();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();

        var dispatcher = BuildDispatcher(dataService, deviceRegistry, commandRegistry);

        var command = new CommandRecord
        {
            Id = 20,
            DeviceId = "123456789",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Parameters = string.Empty,
            CommandTypeId = 1,
            StatusCommandId = 3
        };

        commandRegistry.Upsert(new PendingCommand
        {
            Command = command,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        dispatcher.ConfirmPendingCommandOnNewPosition("123456789");

        // Deve confirmar o comando
        Assert.Equal(2, command.StatusCommandId);
        Assert.Single(dataService.UpdatedCommands);
        Assert.Equal(20, dataService.UpdatedCommands[0].Id);

        // Deve remover do registro (comando concluído)
        Assert.False(commandRegistry.TryGet("123456789", out _));
    }

    [Fact]
    public void ConfirmPendingCommandOnNewPosition_WhenNoPendingCommand_ShouldDoNothing()
    {
        var dataService = new FakeGatewayDataService();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();

        var dispatcher = BuildDispatcher(dataService, deviceRegistry, commandRegistry);

        // Não deve lançar exceção nem registrar update
        dispatcher.ConfirmPendingCommandOnNewPosition("999999999");

        Assert.Empty(dataService.UpdatedCommands);
    }

    [Fact]
    public void ConfirmPendingCommandOnNewPosition_WhenStatus4_ShouldNotConfirmButAllowRetry()
    {
        // Status 4 = falha de envio. Nova posição não deve confirmar; deve
        // cair na lógica de retry (que não reenvia se o timer ainda não expirou).
        var dataService = new FakeGatewayDataService();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();

        var dispatcher = BuildDispatcher(dataService, deviceRegistry, commandRegistry);

        var command = new CommandRecord
        {
            Id = 21,
            DeviceId = "111111111",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Parameters = string.Empty,
            CommandTypeId = 1,
            StatusCommandId = 4
        };

        commandRegistry.Upsert(new PendingCommand
        {
            Command = command,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        // Sem sessão ativa → retry não consegue reenviar
        dispatcher.ConfirmPendingCommandOnNewPosition("111111111");

        // NÃO deve confirmar (status não vira 2)
        Assert.NotEqual(2, command.StatusCommandId);
        Assert.Empty(dataService.UpdatedCommands);

        // Comando deve ainda estar no registro (não foi confirmado)
        Assert.True(commandRegistry.TryGet("111111111", out var still));
        Assert.NotNull(still);
    }

    [Fact]
    public void ConfirmPendingCommandOnNewPosition_WhenStatus3_SetsUpdatedAtUtc()
    {
        var dataService = new FakeGatewayDataService();
        var deviceRegistry = new DeviceRegistry();
        var commandRegistry = new CommandRegistry();
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2025-01-01T10:00:00Z"));

        var dispatcher = BuildDispatcher(dataService, deviceRegistry, commandRegistry, timeProvider: fakeTime);

        var command = new CommandRecord
        {
            Id = 22,
            DeviceId = "555555555",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = null,
            Parameters = string.Empty,
            CommandTypeId = 2,
            StatusCommandId = 3
        };

        commandRegistry.Upsert(new PendingCommand
        {
            Command = command,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        dispatcher.ConfirmPendingCommandOnNewPosition("555555555");

        Assert.Equal(2, command.StatusCommandId);
        Assert.Equal(DateTimeOffset.Parse("2025-01-01T10:00:00Z"), command.UpdatedAtUtc);
    }
}

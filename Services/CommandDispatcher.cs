using System.Net;
using System.Text;
using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Services;

public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly IGatewayDataService _dataService;
    private readonly IPostgresDataService? _postgresDataService;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IUdpTransport _udpTransport;
    private readonly IGatewayMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly GatewayOptions _options;
    private readonly ICommandHubPublisher _commandHubPublisher;

    public CommandDispatcher(
        ILogger<CommandDispatcher> logger,
        IGatewayDataService dataService,
        IDeviceRegistry deviceRegistry,
        ICommandRegistry commandRegistry,
        IUdpTransport udpTransport,
        IGatewayMetrics metrics,
        TimeProvider timeProvider,
        IOptions<GatewayOptions> options,
        ICommandHubPublisher? commandHubPublisher = null,
        IPostgresDataService? postgresDataService = null)
    {
        _logger = logger;
        _dataService = dataService;
        _postgresDataService = postgresDataService;
        _deviceRegistry = deviceRegistry;
        _commandRegistry = commandRegistry;
        _udpTransport = udpTransport;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _options = options.Value;
        _commandHubPublisher = commandHubPublisher ?? new NullCommandHubPublisher();
    }

    public void PollAndDispatch(bool firstPoll)
    {
        var commands = _dataService.GetCommands(firstPoll);
        foreach (var command in commands)
        {
            ProcessCommand(command);
        }

        _metrics.SetCommandsPending(_commandRegistry.Count);
    }

    public void RetryPendingCommandForDevice(string deviceId)
    {
        if (!_commandRegistry.TryGet(deviceId, out var pending) || pending is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (now - pending.UpdatedAtUtc < TimeSpan.FromMilliseconds(_options.Commands.RetryAfterMilliseconds))
        {
            return;
        }

        if (pending.Command.StatusCommandId is not (3 or 4))
        {
            return;
        }

        if (!_deviceRegistry.TryGet(deviceId, out var session) || session is null)
        {
            return;
        }

        var statusAtual = pending.Command.StatusCommandId;
        if (!_dataService.UpdateCommandStatusIfCurrent(pending.Command, statusAtual, 3, now))
        {
            _metrics.IncrementCommandsClaimFailed();
            return;
        }

        pending.Command.StatusCommandId = 3;
        pending.Command.UpdatedAtUtc = now;
        pending.UpdatedAtUtc = now;
        _commandRegistry.Upsert(pending);
        if (!TrySend(session.RemoteEndPoint, pending.Command))
        {
            _commandRegistry.Complete(deviceId);
            var falha = _timeProvider.GetUtcNow();
            if (_dataService.UpdateCommandStatusIfCurrent(pending.Command, 3, 4, falha))
            {
                pending.Command.StatusCommandId = 4;
                pending.Command.UpdatedAtUtc = falha;
            }
        }
    }

    public void ConfirmPendingCommandOnNewPosition(string deviceId)
    {
        if (!_commandRegistry.TryGet(deviceId, out var pending) || pending is null)
        {
            return;
        }

        if (pending.Command.StatusCommandId == 3)
        {
            // Comando foi enviado e o dispositivo reportou nova posição: confirma execução implicitamente.
            var now = _timeProvider.GetUtcNow();
            pending.Command.StatusCommandId = 2;
            pending.Command.UpdatedAtUtc = now;
            _ = _commandHubPublisher.PublishAsync(deviceId, pending.Command.Id, 2);
            _commandRegistry.Complete(deviceId);

            _dataService.UpdateCommand(pending.Command);

            if (_postgresDataService is not null && !ReferenceEquals(_postgresDataService, _dataService))
            {
                try
                {
                    _postgresDataService.UpdateCommand(pending.Command);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "PostgreSQL: falha ao confirmar comando {CommandId} via nova posição STT",
                        pending.Command.Id);
                }
            }

            _metrics.SetCommandsPending(_commandRegistry.Count);
            _logger.LogInformation(
                "Comando {CommandId} tipo {CommandTypeId} para device {DeviceId} confirmado implicitamente por nova posição STT/ALT (status 3→2)",
                pending.Command.Id, pending.Command.CommandTypeId, deviceId);
            return;
        }

        // Para demais status (ex: 4 - falha de envio), usa o retry normal.
        RetryPendingCommandForDevice(deviceId);
    }

    public void HandleResponse(string deviceId, string commandCode1, string commandCode2, string? extraInfo = null)
    {
        var pending = CompletePendingByDeviceId(deviceId, out var matchedDeviceId);
        if (pending is null)
        {
            _logger.LogWarning(
                "Resposta RES recebida para device {DeviceId} ({Code1};{Code2}) mas nenhum comando pendente encontrado",
                deviceId, commandCode1, commandCode2);
            return;
        }

        var expectedCodes = GetExpectedCommandCodes(pending.Command.CommandTypeId);
        var receivedCodes = NormalizeCommandPair(commandCode1, commandCode2);

        if (receivedCodes != expectedCodes)
        {
            _logger.LogWarning(
                "Resposta RES para device {DeviceId} com código {ReceivedCodes} não corresponde ao comando tipo {CommandTypeId} esperado {ExpectedCodes}",
                deviceId, receivedCodes, pending.Command.CommandTypeId, expectedCodes);

            pending.Command.StatusCommandId = 4;
            pending.Command.UpdatedAtUtc = _timeProvider.GetUtcNow();
            _dataService.UpdateCommand(pending.Command);

            if (_postgresDataService is not null && !ReferenceEquals(_postgresDataService, _dataService))
            {
                try
                {
                    _postgresDataService.UpdateCommand(pending.Command);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PostgreSQL: falha ao atualizar comando {CommandId} com status inválido", pending.Command.Id);
                }
            }

            _ = _commandHubPublisher.PublishAsync(pending.Command.DeviceId, pending.Command.Id, 4);
            _metrics.SetCommandsPending(_commandRegistry.Count);
            return;
        }

        if (!string.IsNullOrEmpty(extraInfo) && extraInfo.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Resposta RES para device {DeviceId} indica comando desconhecido: {ExtraInfo}",
                deviceId, extraInfo);

            pending.Command.StatusCommandId = 4;
            pending.Command.UpdatedAtUtc = _timeProvider.GetUtcNow();
            _dataService.UpdateCommand(pending.Command);

            if (_postgresDataService is not null && !ReferenceEquals(_postgresDataService, _dataService))
            {
                try
                {
                    _postgresDataService.UpdateCommand(pending.Command);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PostgreSQL: falha ao atualizar comando {CommandId} rejeitado", pending.Command.Id);
                }
            }

            _ = _commandHubPublisher.PublishAsync(pending.Command.DeviceId, pending.Command.Id, 4);
            _metrics.SetCommandsPending(_commandRegistry.Count);
            return;
        }

        pending.Command.StatusCommandId = 2;
        pending.Command.UpdatedAtUtc = _timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(extraInfo))
        {
            switch (pending.Command.CommandTypeId)
            {
                case 6:
                    pending.Command.Parameters = extraInfo;
                    _logger.LogInformation(
                        "IMSI capturado para device {DeviceId}: {IMSI}",
                        deviceId, extraInfo);
                    break;

                case 7:
                    pending.Command.Parameters = extraInfo;
                    _logger.LogInformation(
                        "ICCID capturado para device {DeviceId}: {ICCID}",
                        deviceId, extraInfo);
                    break;

                case 8:
                    var networkType = MapNetworkType(extraInfo);
                    pending.Command.Parameters = networkType;
                    _logger.LogInformation(
                        "Tipo de rede capturado para device {DeviceId}: {NetworkCode} = {NetworkType}",
                        deviceId, extraInfo, networkType);
                    break;

                case 9:
                    pending.Command.Parameters = extraInfo;
                    _logger.LogInformation(
                        "Número de telefone capturado para device {DeviceId}: {PhoneNumber}",
                        deviceId, extraInfo);
                    break;
            }
        }

        _dataService.UpdateCommand(pending.Command);

        if (_postgresDataService is not null && !ReferenceEquals(_postgresDataService, _dataService))
        {
            try
            {
                _postgresDataService.UpdateCommand(pending.Command);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostgreSQL: falha ao atualizar comando {CommandId} na resposta RES", pending.Command.Id);
            }
        }

        _ = _commandHubPublisher.PublishAsync(pending.Command.DeviceId, pending.Command.Id, 2);
        _metrics.SetCommandsPending(_commandRegistry.Count);
        _logger.LogInformation(
            "Comando {CommandId} tipo {CommandTypeId} para device {DeviceId} concluído com sucesso ({Codes})",
            pending.Command.Id, pending.Command.CommandTypeId, matchedDeviceId, receivedCodes);
    }

    private PendingCommand? CompletePendingByDeviceId(string responseDeviceId, out string matchedDeviceId)
    {
        foreach (var candidate in GetDeviceIdCandidates(responseDeviceId))
        {
            var pending = _commandRegistry.Complete(candidate);
            if (pending is not null)
            {
                matchedDeviceId = candidate;
                return pending;
            }
        }

        matchedDeviceId = responseDeviceId;
        return null;
    }

    private static IEnumerable<string> GetDeviceIdCandidates(string responseDeviceId)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var raw = responseDeviceId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            candidates.Add(raw);
        }

        if (!raw.All(char.IsDigit))
        {
            return candidates;
        }

        var withoutLeadingZeros = raw.TrimStart('0');
        if (string.IsNullOrEmpty(withoutLeadingZeros))
        {
            withoutLeadingZeros = "0";
        }

        candidates.Add(withoutLeadingZeros);
        if (raw.Length == 9)
        {
            candidates.Add(raw.PadLeft(10, '0'));
        }

        if (withoutLeadingZeros.Length == 9)
        {
            candidates.Add(withoutLeadingZeros.PadLeft(10, '0'));
        }

        return candidates;
    }

    private void ProcessCommand(CommandRecord command)
    {
        switch (command.StatusCommandId)
        {
            case 1:
            case 3:
            case 4:
                SendIfPossible(command);
                break;
            case 5:
                CancelIfNecessary(command);
                break;
        }
    }

    private void SendIfPossible(CommandRecord command)
    {
        if (_commandRegistry.TryGet(command.DeviceId, out var existing) && existing is not null && existing.Command.Id != command.Id)
        {
            return;
        }

        if (!_deviceRegistry.TryGet(command.DeviceId, out var session) || session is null)
        {
            return;
        }

        var agora = _timeProvider.GetUtcNow();
        var statusAtual = command.StatusCommandId;
        if (!_dataService.UpdateCommandStatusIfCurrent(command, statusAtual, 3, agora))
        {
            _metrics.IncrementCommandsClaimFailed();
            return;
        }

        command.StatusCommandId = 3;
        command.UpdatedAtUtc = agora;
        _commandRegistry.Upsert(new PendingCommand
        {
            Command = command,
            UpdatedAtUtc = agora
        });
        _ = _commandHubPublisher.PublishAsync(command.DeviceId, command.Id, 3);

        if (!TrySend(session.RemoteEndPoint, command))
        {
            _commandRegistry.Complete(command.DeviceId);
            var falha = _timeProvider.GetUtcNow();
            if (_dataService.UpdateCommandStatusIfCurrent(command, 3, 4, falha))
            {
                command.StatusCommandId = 4;
                command.UpdatedAtUtc = falha;
                _ = _commandHubPublisher.PublishAsync(command.DeviceId, command.Id, 4);
            }
        }
    }

    private void CancelIfNecessary(CommandRecord command)
    {
        if (_dataService.UpdateCommandStatusIfCurrent(command, 5, 6, _timeProvider.GetUtcNow()))
        {
            _commandRegistry.Complete(command.DeviceId);
        }
    }

    private bool TrySend(IPEndPoint remoteEndPoint, CommandRecord command)
    {
        try
        {
            var payload = Encoding.ASCII.GetBytes(BuildPayload(command));
            _udpTransport.SendAsync(payload, remoteEndPoint, CancellationToken.None).GetAwaiter().GetResult();
            _metrics.IncrementCommandsSent();
            _logger.LogInformation("Comando enviado ao device {DeviceId} para {RemoteEndPoint}", command.DeviceId, remoteEndPoint);
            return true;
        }
        catch (Exception ex)
        {
            _metrics.IncrementCommandsFailed();
            _logger.LogError(ex, "Falha ao enviar comando {CommandId} para device {DeviceId}", command.Id, command.DeviceId);
            return false;
        }
    }

    private static string BuildPayload(CommandRecord command)
    {
        if (command.CommandTypeId == 10)
        {
            return command.Parameters;
        }

        var deviceId = command.DeviceId.Length == 9 ? command.DeviceId.PadLeft(10, '0') : command.DeviceId;
        return command.CommandTypeId switch
        {
            1 => $"CMD;{deviceId};04;01",
            2 => $"CMD;{deviceId};04;02",
            3 => $"CMD;{deviceId};03;01",
            4 => $"CMD;{deviceId};01;03",
            5 => $"CMD;{deviceId};01;01",
            6 => $"CMD;{deviceId};01;02",
            7 => $"CMD;{deviceId};01;03",
            8 => $"CMD;{deviceId};01;04",
            9 => $"CMD;{deviceId};01;05",
            _ => throw new InvalidOperationException($"Tipo de comando não suportado: {command.CommandTypeId}")
        };
    }

    private static string GetExpectedCommandCodes(int commandTypeId)
    {
        return commandTypeId switch
        {
            1 => NormalizeCommandPair("04", "01"),
            2 => NormalizeCommandPair("04", "02"),
            3 => NormalizeCommandPair("03", "01"),
            4 => NormalizeCommandPair("01", "03"),
            5 => NormalizeCommandPair("01", "01"),
            6 => NormalizeCommandPair("01", "02"),
            7 => NormalizeCommandPair("01", "03"),
            8 => NormalizeCommandPair("01", "04"),
            9 => NormalizeCommandPair("01", "05"),
            _ => throw new InvalidOperationException($"Tipo de comando sem código esperado: {commandTypeId}")
        };
    }

    private static string NormalizeCommandPair(string code1, string code2)
    {
        return $"{NormalizeCodePart(code1)};{NormalizeCodePart(code2)}";
    }

    private static string NormalizeCodePart(string code)
    {
        var trimmed = code?.Trim() ?? string.Empty;
        if (int.TryParse(trimmed, out var parsed) && parsed >= 0)
        {
            return parsed.ToString("00");
        }

        return trimmed.ToUpperInvariant();
    }

    private static string MapNetworkType(string code)
    {
        if (!int.TryParse(code?.Trim(), out var networkCode))
        {
            return "Unknown";
        }

        return networkCode switch
        {
            0 => "GSM",
            1 => "GSM COMPACT",
            2 => "UTRAN",
            3 => "GSM with EDGE availability",
            4 => "UTRAN with HSDPA availability",
            5 => "UTRAN with HSUPA availability",
            6 => "UTRAN with HSDPA and HSUPA availability",
            7 => "Reserved",
            8 => "LTE Cat M1",
            9 => "LTE Cat NB1",
            10 => "LTE Cat 1",
            255 => "Invalid or No Network",
            _ => $"Unknown ({networkCode})"
        };
    }
}
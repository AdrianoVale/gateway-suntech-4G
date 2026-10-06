using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Logging;

namespace GatewaySunteh4G_NET8.Services;

public sealed class St4315PacketProcessor : IGatewayPacketProcessor
{
    private const int MaxDeviceIdDigits = 19;
    // Cache em memória da última leitura válida de bateria por device (bat_main 10–25V, bat_back 2–6V).
    // Evita consulta ao banco em cada pacote ALT; o banco é consultado apenas no primeiro miss por device.
    private readonly ConcurrentDictionary<string, (double BatMain, double BatBack)> _batteryCache = new();
    private readonly ILogger<St4315PacketProcessor> _logger;
    private readonly IGatewayMetrics _metrics;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IPositionPersistenceService _positionPersistenceService;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IPositionHubPublisher _hubPublisher;
    private readonly INotifEventPublisher _notifPublisher;

    public St4315PacketProcessor(
        ILogger<St4315PacketProcessor> logger,
        IGatewayMetrics metrics,
        IDeviceRegistry deviceRegistry,
        ICommandRegistry commandRegistry,
        IPositionPersistenceService positionPersistenceService,
        ICommandDispatcher commandDispatcher,
        IPositionHubPublisher hubPublisher,
        INotifEventPublisher notifPublisher)
    {
        _logger = logger;
        _metrics = metrics;
        _deviceRegistry = deviceRegistry;
        _commandRegistry = commandRegistry;
        _positionPersistenceService = positionPersistenceService;
        _commandDispatcher = commandDispatcher;
        _hubPublisher = hubPublisher;
        _notifPublisher = notifPublisher;
    }

    public Task ProcessAsync(UdpEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message = Encoding.ASCII.GetString(envelope.Payload).Trim('\0', '\r', '\n', ' ', '"');
        if (string.IsNullOrWhiteSpace(message))
        {
            _metrics.IncrementDecodeErrors();
            _logger.LogWarning("Pacote vazio recebido de {RemoteEndPoint}", envelope.RemoteEndPoint);
            return Task.CompletedTask;
        }

        var fields = message.Split(';', StringSplitOptions.None);
        var header = fields[0].Trim().ToUpperInvariant();

        switch (header)
        {
            case "STT":
                ProcessTelemetry("STT", fields, envelope.RemoteEndPoint, message, envelope.ReceivedAtUtc);
                return Task.CompletedTask;
            case "ALT":
                ProcessTelemetry("ALT", fields, envelope.RemoteEndPoint, message, envelope.ReceivedAtUtc);
                return Task.CompletedTask;
            case "ALV":
                ProcessAlive(fields, envelope.RemoteEndPoint, message, envelope.ReceivedAtUtc);
                return Task.CompletedTask;
            case "UEX":
            case "AUEX":
                ProcessExternalData(header, fields, envelope.RemoteEndPoint, message, envelope.ReceivedAtUtc);
                return Task.CompletedTask;
            case "TRV":
            case "ATRV":
                ProcessTravel(header, fields, envelope.RemoteEndPoint, message, envelope.ReceivedAtUtc);
                return Task.CompletedTask;
            case "RES":
                ProcessResponse(fields, envelope.RemoteEndPoint);
                return Task.CompletedTask;
            default:
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning("Header não suportado {Header} de {RemoteEndPoint}: {Message}", header, envelope.RemoteEndPoint, message);
                return Task.CompletedTask;
        }
    }

    private void ProcessTelemetry(string header, string[] fields, IPEndPoint remoteEndPoint, string rawMessage, DateTimeOffset receivedAtUtc)
    {
        try
        {
            // Formato estendido: data (YYYYMMDD) aparece na posição 6; formato curto traz lat/lon antes.
            var isExtended = IsExtendedTelemetryPacket(fields);
            var deviceId = GetField(fields, 1);
            var model = GetField(fields, 3);
            var date = GetField(fields, isExtended ? 6 : 3);
            var time = GetField(fields, isExtended ? 7 : 4);
            var latitude = GetField(fields, isExtended ? 13 : 5);
            var longitude = GetField(fields, isExtended ? 14 : 6);
            var speedField = GetField(fields, isExtended ? 15 : 7);
            var degreeField = GetField(fields, isExtended ? 16 : 8);
            var satField = GetField(fields, isExtended ? 17 : 9);
            var fixField = GetField(fields, isExtended ? 18 : 10);
            var inputField = GetField(fields, isExtended ? 19 : 11);
            var outputField = GetField(fields, isExtended ? 20 : 12);
            var modeField = GetField(fields, isExtended ? 21 : 13);
            // No formato estendido: layout final é [...;bat_back;bat_main;hex_footer]
            // Apenas valores decimais (ex: "13.18", "4.2") são tensão; inteiros nessa posição
            // são odômetro, sinal ou outros campos que não devem ser persistidos como bateria.
            var (batteryField, batteryBackupField) = ResolveTelemetryBatteryFields(fields, isExtended);
            _logger.LogInformation("Pacote bruto recebido de {RemoteEndPoint}: {RawMessage} em {ReceivedAtUtc}", remoteEndPoint, rawMessage, receivedAtUtc);
            _logger.LogDebug(
                "Pacote {Header} device {DeviceId}: isExtended={IsExtended} campos={FieldCount} batMainField=[{BatMain}] batBackField=[{BatBack}]",
                header, deviceId, isExtended, fields.Length, batteryField, batteryBackupField);

            if (!IsSupportedDeviceId(deviceId))
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Pacote {Header} ignorado para device_id invalido {DeviceId} de {RemoteEndPoint}. Esperado: numerico com ate {MaxDigits} digitos.",
                    header,
                    deviceId,
                    remoteEndPoint,
                    MaxDeviceIdDigits);
                return;
            }

            var deviceTimestampUtc = ParseUtcTimestamp(date, time);
            _deviceRegistry.Upsert(new DeviceSession
            {
                DeviceId = deviceId,
                Header = header,
                Model = model,
                RemoteEndPoint = remoteEndPoint,
                LastSeenUtc = receivedAtUtc,
                DeviceTimestampUtc = deviceTimestampUtc,
                Latitude = latitude,
                Longitude = longitude,
                RawMessage = rawMessage,
                LastMessageBytes = envelopeBytes(rawMessage)
            });

            _metrics.IncrementMessagesDecoded();

            // Calcular bat_main e bat_back antes de montar o PositionRecord.
            // Pacotes ALT geralmente não carregam tensão de bateria; nesses casos,
            // reaproveitar a última leitura válida (cache em memória → fallback banco).
            var batMain = ParseDouble(batteryField);
            var batBack = ParseNullableDouble(batteryBackupField);

            if (batMain > 0)
            {
                // Pacote tem tensão válida — atualizar cache para reutilização futura
                _batteryCache[deviceId] = (batMain, batBack);
            }
            else
            {
                // Sem dados de bateria no pacote — buscar último valor válido
                if (!_batteryCache.TryGetValue(deviceId, out var cached))
                {
                    // Cache miss: consultar banco (apenas na primeira ocorrência após reinicialização)
                    var dbBattery = _positionPersistenceService.GetLastValidBattery(deviceId);
                    if (dbBattery.HasValue)
                    {
                        _batteryCache[deviceId] = dbBattery.Value;
                        cached = dbBattery.Value;
                    }
                    // Se o banco também não tiver histórico, cached permanece (0,0)
                }
                if (cached.BatMain > 0)
                {
                    batMain = cached.BatMain;
                    batBack = cached.BatBack;
                    _logger.LogDebug(
                        "Bateria reaproveitada do histórico para device {DeviceId}: batMain={BatMain} batBack={BatBack}",
                        deviceId, batMain, batBack);
                }
            }

            var positionRecord = new PositionRecord
            {
                DeviceId      = deviceId,
                DatetimeUtc   = deviceTimestampUtc ?? receivedAtUtc,
                Latitude      = ParseDouble(latitude),
                Longitude     = ParseDouble(longitude),
                Speed         = NormalizeSpeed(speedField),
                Degree        = ParseDouble(degreeField),
                Gps           = fixField == "1",
                Sat           = ParseInt(satField),
                Ign           = ReadFlag(inputField, inputField.Length - 1),
                Block         = ReadFlag(outputField, outputField.Length - 1),
                Io            = NormalizeIo(outputField),
                BatMain       = batMain,
                BatBack       = batBack,
                Storage       = false,
                MsgTypeId     = MapMessageType(header, modeField),
                DeviceModelId = ParseInt(model)
            };
            _positionPersistenceService.PersistOrCache(positionRecord);
            // Fire-and-forget: falha no Hub não bloqueia o fluxo principal
            _ = _hubPublisher.PublishAsync(deviceId, positionRecord);
            // Fire-and-forget: eventos de alerta (pânico/bateria/etc.) p/ a Central de Notificações
            _notifPublisher.PublishDeviceEvent(deviceId, positionRecord.MsgTypeId);
            _metrics.SetDevicesConnected(_deviceRegistry.Count);
            _metrics.SetCommandsPending(_commandRegistry.Count);
            _positionPersistenceService.PendingCacheCount();
            // Nova posição STT/ALT confirma comandos pendentes em status 3 (enviado, aguardando RES).
            _commandDispatcher.ConfirmPendingCommandOnNewPosition(deviceId);

            _logger.LogInformation(
                "Pacote {Header} processado para device {DeviceId} de {RemoteEndPoint} lat={Latitude} lon={Longitude}",
                header,
                deviceId,
                remoteEndPoint,
                latitude,
                longitude);
        }
        catch (Exception ex)
        {
            _metrics.IncrementDecodeErrors();
            _logger.LogError(ex, "Falha ao processar pacote {Header} de {RemoteEndPoint}", header, remoteEndPoint);
        }
    }

    private void ProcessResponse(string[] fields, IPEndPoint remoteEndPoint)
    {
        try
        {
            if (fields.Length < 4)
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Resposta RES inválida de {RemoteEndPoint}: esperado mínimo 4 campos (RES;IMEI;CODE1;CODE2), recebido {FieldCount}",
                    remoteEndPoint, fields.Length);
                return;
            }

            var deviceId = GetField(fields, 1);
            var commandCode1 = GetField(fields, 2);
            var commandCode2 = GetField(fields, 3);
            var extraInfo = fields.Length > 4 ? GetOptionalField(fields, 4) : null;

            _commandDispatcher.HandleResponse(deviceId, commandCode1, commandCode2, extraInfo);
            _metrics.IncrementMessagesDecoded();
            _metrics.SetCommandsPending(_commandRegistry.Count);
            _logger.LogInformation(
                "Resposta RES recebida do device {DeviceId} em {RemoteEndPoint}: {Code1};{Code2} {Extra}",
                deviceId, remoteEndPoint, commandCode1, commandCode2, extraInfo ?? "");
        }
        catch (Exception ex)
        {
            _metrics.IncrementDecodeErrors();
            _logger.LogError(ex, "Falha ao processar RES de {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private void ProcessAlive(string[] fields, IPEndPoint remoteEndPoint, string rawMessage, DateTimeOffset receivedAtUtc)
    {
        try
        {
            if (fields.Length < 2)
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Pacote ALV inválido de {RemoteEndPoint}: esperado mínimo 2 campos (ALV;IMEI), recebido {FieldCount}",
                    remoteEndPoint,
                    fields.Length);
                return;
            }

            var deviceId = GetField(fields, 1);
            if (!IsSupportedDeviceId(deviceId))
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Pacote ALV ignorado para device_id invalido {DeviceId} de {RemoteEndPoint}. Esperado: numerico com ate {MaxDigits} digitos.",
                    deviceId,
                    remoteEndPoint,
                    MaxDeviceIdDigits);
                return;
            }

            _deviceRegistry.Upsert(new DeviceSession
            {
                DeviceId = deviceId,
                Header = "ALV",
                Model = string.Empty,
                RemoteEndPoint = remoteEndPoint,
                LastSeenUtc = receivedAtUtc,
                DeviceTimestampUtc = null,
                Latitude = null,
                Longitude = null,
                RawMessage = rawMessage,
                LastMessageBytes = envelopeBytes(rawMessage)
            });

            _metrics.IncrementMessagesDecoded();
            _metrics.SetDevicesConnected(_deviceRegistry.Count);
            _metrics.SetCommandsPending(_commandRegistry.Count);
            _commandDispatcher.RetryPendingCommandForDevice(deviceId);

            _logger.LogInformation("Pacote ALV processado para device {DeviceId} de {RemoteEndPoint}", deviceId, remoteEndPoint);
        }
        catch (Exception ex)
        {
            _metrics.IncrementDecodeErrors();
            _logger.LogError(ex, "Falha ao processar pacote ALV de {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private void ProcessExternalData(string header, string[] fields, IPEndPoint remoteEndPoint, string rawMessage, DateTimeOffset receivedAtUtc)
    {
        try
        {
            if (fields.Length < 23)
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Pacote {Header} inválido de {RemoteEndPoint}: esperado mínimo 23 campos, recebido {FieldCount}",
                    header,
                    remoteEndPoint,
                    fields.Length);
                return;
            }

            var deviceId = GetField(fields, 1);
            var model = GetField(fields, 3);
            var msgTypeField = GetField(fields, 5);
            var date = GetField(fields, 6);
            var time = GetField(fields, 7);
            var latitude = GetField(fields, 13);
            var longitude = GetField(fields, 14);
            var speedField = GetField(fields, 15);
            var degreeField = GetField(fields, 16);
            var satField = GetField(fields, 17);
            var fixField = GetField(fields, 18);
            var inputField = GetField(fields, 19);
            var outputField = GetField(fields, 20);
            var batteryField = ResolveExternalBatteryField(fields);
            var batteryBackupField = ResolveExternalBackupBatteryField(fields);

            if (!IsSupportedDeviceId(deviceId))
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Pacote {Header} ignorado para device_id invalido {DeviceId} de {RemoteEndPoint}. Esperado: numerico com ate {MaxDigits} digitos.",
                    header,
                    deviceId,
                    remoteEndPoint,
                    MaxDeviceIdDigits);
                return;
            }

            var canonicalHeader = NormalizeHeader(header);
            var deviceTimestampUtc = ParseUtcTimestamp(date, time);

            _deviceRegistry.Upsert(new DeviceSession
            {
                DeviceId = deviceId,
                Header = canonicalHeader,
                Model = model,
                RemoteEndPoint = remoteEndPoint,
                LastSeenUtc = receivedAtUtc,
                DeviceTimestampUtc = deviceTimestampUtc,
                Latitude = latitude,
                Longitude = longitude,
                RawMessage = rawMessage,
                LastMessageBytes = envelopeBytes(rawMessage)
            });

            _metrics.IncrementMessagesDecoded();
            _positionPersistenceService.PersistOrCache(new PositionRecord
            {
                DeviceId = deviceId,
                DatetimeUtc = deviceTimestampUtc ?? receivedAtUtc,
                Latitude = ParseDouble(latitude),
                Longitude = ParseDouble(longitude),
                Speed = NormalizeSpeed(speedField),
                Degree = ParseDouble(degreeField),
                Gps = fixField == "1",
                Sat = ParseInt(satField),
                Ign = ReadFlag(inputField, inputField.Length - 1),
                Block = ReadFlag(outputField, outputField.Length - 1),
                Io = NormalizeIo(outputField),
                BatMain = ParseDouble(batteryField),
                BatBack = ParseNullableDouble(batteryBackupField),
                Storage = msgTypeField == "0",
                MsgTypeId = MapMessageType(canonicalHeader, msgTypeField),
                DeviceModelId = ParseInt(model)
            });

            _metrics.SetDevicesConnected(_deviceRegistry.Count);
            _metrics.SetCommandsPending(_commandRegistry.Count);
            _positionPersistenceService.PendingCacheCount();
            _commandDispatcher.RetryPendingCommandForDevice(deviceId);

            _logger.LogInformation(
                "Pacote {Header} processado para device {DeviceId} de {RemoteEndPoint} lat={Latitude} lon={Longitude}",
                canonicalHeader,
                deviceId,
                remoteEndPoint,
                latitude,
                longitude);
        }
        catch (Exception ex)
        {
            _metrics.IncrementDecodeErrors();
            _logger.LogError(ex, "Falha ao processar pacote {Header} de {RemoteEndPoint}", header, remoteEndPoint);
        }
    }

    private void ProcessTravel(string header, string[] fields, IPEndPoint remoteEndPoint, string rawMessage, DateTimeOffset receivedAtUtc)
    {
        try
        {
            if (fields.Length < 22)
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Pacote {Header} inválido de {RemoteEndPoint}: esperado mínimo 22 campos, recebido {FieldCount}",
                    header,
                    remoteEndPoint,
                    fields.Length);
                return;
            }

            var deviceId = GetField(fields, 1);
            var model = GetField(fields, 3);
            var msgTypeField = GetField(fields, 5);
            var date = GetField(fields, 6);
            var time = GetField(fields, 7);
            var latitudeStart = GetField(fields, 8);
            var longitudeStart = GetField(fields, 9);
            var latitudeFinish = GetField(fields, 10);
            var longitudeFinish = GetField(fields, 11);
            var avgSpeedField = GetTravelAverageSpeedField(fields);
            var batteryField = ResolveTravelBatteryField(fields);
            var batteryBackupField = ResolveTravelBackupBatteryField(fields);

            if (!IsSupportedDeviceId(deviceId))
            {
                _metrics.IncrementDecodeErrors();
                _logger.LogWarning(
                    "Pacote {Header} ignorado para device_id invalido {DeviceId} de {RemoteEndPoint}. Esperado: numerico com ate {MaxDigits} digitos.",
                    header,
                    deviceId,
                    remoteEndPoint,
                    MaxDeviceIdDigits);
                return;
            }

            var canonicalHeader = NormalizeHeader(header);
            var deviceTimestampUtc = ParseUtcTimestamp(date, time);
            var hasFinishCoordinates = !string.IsNullOrWhiteSpace(latitudeFinish) && !string.IsNullOrWhiteSpace(longitudeFinish);
            var latitude = hasFinishCoordinates ? latitudeFinish : latitudeStart;
            var longitude = hasFinishCoordinates ? longitudeFinish : longitudeStart;

            _deviceRegistry.Upsert(new DeviceSession
            {
                DeviceId = deviceId,
                Header = canonicalHeader,
                Model = model,
                RemoteEndPoint = remoteEndPoint,
                LastSeenUtc = receivedAtUtc,
                DeviceTimestampUtc = deviceTimestampUtc,
                Latitude = latitude,
                Longitude = longitude,
                RawMessage = rawMessage,
                LastMessageBytes = envelopeBytes(rawMessage)
            });

            _metrics.IncrementMessagesDecoded();
            _positionPersistenceService.PersistOrCache(new PositionRecord
            {
                DeviceId = deviceId,
                DatetimeUtc = deviceTimestampUtc ?? receivedAtUtc,
                Latitude = ParseDouble(latitude),
                Longitude = ParseDouble(longitude),
                Speed = NormalizeSpeed(avgSpeedField),
                Degree = 0d,
                Gps = true,
                Sat = 0,
                Ign = false,
                Block = false,
                Io = "000000",
                BatMain = ParseDouble(batteryField),
                BatBack = ParseNullableDouble(batteryBackupField),
                Storage = msgTypeField == "0",
                MsgTypeId = MapMessageType(canonicalHeader, msgTypeField),
                DeviceModelId = ParseInt(model)
            });

            _metrics.SetDevicesConnected(_deviceRegistry.Count);
            _metrics.SetCommandsPending(_commandRegistry.Count);
            _positionPersistenceService.PendingCacheCount();
            _commandDispatcher.RetryPendingCommandForDevice(deviceId);

            _logger.LogInformation(
                "Pacote {Header} processado para device {DeviceId} de {RemoteEndPoint} lat={Latitude} lon={Longitude}",
                canonicalHeader,
                deviceId,
                remoteEndPoint,
                latitude,
                longitude);
        }
        catch (Exception ex)
        {
            _metrics.IncrementDecodeErrors();
            _logger.LogError(ex, "Falha ao processar pacote {Header} de {RemoteEndPoint}", header, remoteEndPoint);
        }
    }

    private static string GetField(string[] fields, int index)
    {
        if (index >= fields.Length)
        {
            throw new InvalidOperationException($"Campo obrigatório na posição {index} não encontrado.");
        }

        return fields[index].Trim();
    }

    private static string GetOptionalField(string[] fields, int index)
    {
        return index < fields.Length ? fields[index].Trim() : string.Empty;
    }

    private static DateTimeOffset? ParseUtcTimestamp(string date, string time)
    {
        if (date.Length != 8 || time.Length != 8)
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                string.Concat(date, time),
                "yyyyMMddHH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return new DateTimeOffset(parsed, TimeSpan.Zero);
    }

    private static byte[] envelopeBytes(string rawMessage)
    {
        return Encoding.ASCII.GetBytes(rawMessage);
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static int NormalizeSpeed(string value)
    {
        var parsed = (int)Math.Round(ParseDouble(value), MidpointRounding.AwayFromZero);
        return parsed < 6 ? 0 : parsed;
    }

    private static double ParseDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        // Alguns devices enviam decimal com vírgula; normaliza sem quebrar o formato com ponto.
        var normalized = value?.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : 0d;
    }

    private static double ParseNullableDouble(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? 0d : ParseDouble(value);
    }

    private static bool ReadFlag(string field, int index)
    {
        return index >= 0 && index < field.Length && field[index] == '1';
    }

    private static string NormalizeIo(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return "000000";
        }

        return field.Length >= 6 ? field[^6..] : field.PadLeft(6, '0');
    }

    private static int MapMessageType(string header, string modeField)
    {
        var mode = ParseInt(modeField);
        return header switch
        {
            "STT" => mode + 1000,
            "ALT" => mode + 4000,
            "UEX" => mode + 5000,
            "TRV" => mode + 6000,
            _ => mode
        };
    }

    private static string NormalizeHeader(string header)
    {
        return header switch
        {
            "AUEX" => "UEX",
            "ATRV" => "TRV",
            _ => header
        };
    }

    /// <summary>
    /// Resolve bat_main e bat_back em pacotes STT/ALT para ambos os layouts estendidos conhecidos.
    /// <br/>Layout antigo: <c>[...;bat_back;bat_main;hex_footer]</c> — bat_main é o penúltimo campo.
    /// <br/>Layout novo:  <c>[...;bat_main;bat_back;campos_extras...]</c> — par consecutivo de tensões
    ///   a partir do índice 22, sem hex_footer obrigatório no fim.
    /// </summary>
    private static (string BatMain, string BatBack) ResolveTelemetryBatteryFields(string[] fields, bool isExtended)
    {
        if (!isExtended)
        {
            // Formato não estendido: bat_main em índice 24 (ou 21 como fallback), bat_back em 22.
            var primary = GetOptionalField(fields, 24);
            var batMain = !string.IsNullOrWhiteSpace(primary) ? primary : GetOptionalField(fields, 21);
            return (batMain, GetOptionalField(fields, 22));
        }

        // Formato estendido — layout antigo: bat_main é o penúltimo campo (antes de hex_footer).
        // Exemplos:
        //   ...;800003;3.6;11.94;500000193E0CCD01  (28 campos → bat_main índice 26)
        //   ...;3.6;11.94;500000193E0CCD01          (27 campos → bat_main índice 25)
        var endCandidate = GetOptionalField(fields, fields.Length - 2);
        if (IsVoltageValue(endCandidate))
        {
            var backCandidate = GetOptionalField(fields, fields.Length - 3);
            return (endCandidate, IsVoltageValue(backCandidate) ? backCandidate : string.Empty);
        }

        // Layout novo: sem hex_footer fixo no fim; bat_main e bat_back são o primeiro par
        // de tensões consecutivas encontrado a partir do índice 22.
        // Exemplo:
        //   ...;1;0068;;0003800F;13.29;4.0;;10;5916;5916;43  (33 campos → bat_main índice 26)
        for (var i = 22; i < fields.Length; i++)
        {
            var candidate = GetOptionalField(fields, i);
            if (!IsVoltageValue(candidate))
                continue;
            var nextField = GetOptionalField(fields, i + 1);
            return (candidate, IsVoltageValue(nextField) ? nextField : string.Empty);
        }

        return (string.Empty, string.Empty);
    }

    /// <summary>
    /// Retorna true se o valor parece ser uma tensão de bateria (contém ponto decimal
    /// e está dentro da faixa razoável para sistemas veiculares: 0–100 V).
    /// Valores inteiros como odômetro (1905368) ou percentual de sinal (71) retornam false.
    /// </summary>
    private static bool IsVoltageValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return trimmed.Contains('.') &&
               double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
               v >= 0.0 && v <= 100.0;
    }

    /// <summary>
    /// Detecta formato estendido pelo padrão de data YYYYMMDD na posição 6.
    /// Formato estendido: STT;device;mask;model;info;msgtype;YYYYMMDD;HH:mm:ss;...
    /// Formato curto:     STT;device;[hex;]date;time;lat;lon;...
    /// </summary>
    private static bool IsExtendedTelemetryPacket(string[] fields)
    {
        if (fields.Length <= 6)
        {
            return false;
        }

        var candidate = fields[6].Trim();
        return candidate.Length == 8 && candidate.All(char.IsDigit);
    }

    private static string ResolveExternalBatteryField(string[] fields)
    {
        // UEX/AUEX costuma trazer bateria principal no final do payload.
        return GetOptionalField(fields, fields.Length - 1);
    }

    private static string ResolveExternalBackupBatteryField(string[] fields)
    {
        // Campo imediatamente anterior pode conter bateria backup em alguns layouts.
        return GetOptionalField(fields, fields.Length - 2);
    }

    private static string ResolveTravelBatteryField(string[] fields)
    {
        // TRV/ATRV pode trazer bateria principal no final do payload (quando disponível).
        return GetOptionalField(fields, fields.Length - 1);
    }

    private static string ResolveTravelBackupBatteryField(string[] fields)
    {
        return GetOptionalField(fields, fields.Length - 2);
    }

    private static string GetTravelAverageSpeedField(string[] fields)
    {
        var decimalFields = new List<string>(capacity: 2);
        for (var i = 19; i < fields.Length; i++)
        {
            var candidate = fields[i].Trim();
            if (!candidate.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (double.TryParse(candidate, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
            {
                decimalFields.Add(candidate);
                if (decimalFields.Count == 2)
                {
                    return decimalFields[1];
                }
            }
        }

        return decimalFields.Count == 1 ? decimalFields[0] : GetOptionalField(fields, 20);
    }

    private static bool IsSupportedDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > MaxDeviceIdDigits)
        {
            return false;
        }

        if (!deviceId.All(char.IsDigit))
        {
            return false;
        }

        return decimal.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }
}
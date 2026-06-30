using System.Globalization;
using System.Net;
using System.Text;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Logging;

namespace GatewaySunteh4G_NET8.Services;

public sealed class St4315PacketProcessor : IGatewayPacketProcessor
{
    private const int MaxDeviceIdDigits = 19;
    private readonly ILogger<St4315PacketProcessor> _logger;
    private readonly IGatewayMetrics _metrics;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IPositionPersistenceService _positionPersistenceService;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IPositionHubPublisher _hubPublisher;

    public St4315PacketProcessor(
        ILogger<St4315PacketProcessor> logger,
        IGatewayMetrics metrics,
        IDeviceRegistry deviceRegistry,
        ICommandRegistry commandRegistry,
        IPositionPersistenceService positionPersistenceService,
        ICommandDispatcher commandDispatcher,
        IPositionHubPublisher hubPublisher)
    {
        _logger = logger;
        _metrics = metrics;
        _deviceRegistry = deviceRegistry;
        _commandRegistry = commandRegistry;
        _positionPersistenceService = positionPersistenceService;
        _commandDispatcher = commandDispatcher;
        _hubPublisher = hubPublisher;
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
            var isExtended = fields.Length > 28;
            var deviceId = GetField(fields, 1);
            var model = GetField(fields, 3);
            var date = GetField(fields, 6);
            var time = GetField(fields, 7);
            var latitude = GetField(fields, isExtended ? 13 : 8);
            var longitude = GetField(fields, isExtended ? 14 : 9);
            var speedField = GetField(fields, isExtended ? 15 : 10);
            var degreeField = GetField(fields, isExtended ? 16 : 11);
            var satField = GetField(fields, isExtended ? 17 : 12);
            var fixField = GetField(fields, isExtended ? 18 : 13);
            var inputField = GetField(fields, isExtended ? 19 : 14);
            var outputField = GetField(fields, isExtended ? 20 : 15);
            var modeField = GetField(fields, isExtended ? 21 : 16);
            var batteryField = GetField(fields, isExtended ? 24 : 21);
            var batteryBackupField = GetOptionalField(fields, isExtended ? 25 : 22);

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
                BatMain       = ParseDouble(batteryField),
                BatBack       = ParseNullableDouble(batteryBackupField),
                Storage       = false,
                MsgTypeId     = MapMessageType(header, modeField),
                DeviceModelId = ParseInt(model)
            };
            _positionPersistenceService.PersistOrCache(positionRecord);
            // Fire-and-forget: falha no Hub não bloqueia o fluxo principal
            _ = _hubPublisher.PublishAsync(deviceId, positionRecord);
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
                BatMain = 0d,
                BatBack = 0d,
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
                BatMain = 0d,
                BatBack = 0d,
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
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
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
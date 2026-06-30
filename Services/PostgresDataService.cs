using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using System.Globalization;
using System.IO;
using System.Net.Sockets;

namespace GatewaySunteh4G_NET8.Services;

public sealed class PostgresDataService : IPostgresDataService
{
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);
    private readonly ILogger<PostgresDataService> _logger;
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;
    private readonly int _maxRetryAttempts;
    private readonly int _retryBaseDelayMilliseconds;
    private readonly int _retryMaxDelayMilliseconds;
    private readonly int _circuitOpenSeconds;
    private long _circuitOpenUntilUnixMs;

    public PostgresDataService(ILogger<PostgresDataService> logger, IOptions<GatewayOptions> options)
    {
        _logger = logger;
        var postgresOptions = options.Value.PostgresDatabase;
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(postgresOptions.ConnectionString)
        {
            Timeout = postgresOptions.ConnectTimeoutSeconds,
            CommandTimeout = postgresOptions.CommandTimeoutSeconds,
            Pooling = true,
            KeepAlive = postgresOptions.KeepAliveSeconds
        };

        _connectionString = connectionStringBuilder.ConnectionString;
        _commandTimeoutSeconds = postgresOptions.CommandTimeoutSeconds;
        _maxRetryAttempts = postgresOptions.MaxRetryAttempts;
        _retryBaseDelayMilliseconds = postgresOptions.RetryBaseDelayMilliseconds;
        _retryMaxDelayMilliseconds = postgresOptions.RetryMaxDelayMilliseconds;
        _circuitOpenSeconds = postgresOptions.CircuitOpenSeconds;
    }

    public bool InsertPosition(PositionRecord position)
    {
        const string sql = @"INSERT INTO position
(device_id, datetime, lat, lon, speed, degree, gps, sat, ign, block, io, bat_main, bat_back, storage, msg_type_id, device_model_id)
VALUES (@device_id, @datetime, @lat, @lon, @speed, @degree, @gps, @sat, @ign, @block, @io, @bat_main, @bat_back, @storage, @msg_type_id, @device_model_id)";

        if (!decimal.TryParse(position.DeviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDeviceId))
        {
            _logger.LogWarning("PostgreSQL: device_id invalido para insercao de posicao: {DeviceId}", position.DeviceId);
            return false;
        }

        if (IsCircuitOpen())
        {
            return false;
        }

        try
        {
            var inserted = ExecuteWithRetry(() =>
            {
                using var connection = OpenConnection();
                using var command = CreateCommand(connection, sql);
                AddParameter(command, "@device_id", NpgsqlDbType.Numeric, parsedDeviceId);
                AddParameter(command, "@datetime", NpgsqlDbType.Timestamp, ToDatabaseDateTime(position.DatetimeUtc));
                AddParameter(command, "@lat", NpgsqlDbType.Double, position.Latitude);
                AddParameter(command, "@lon", NpgsqlDbType.Double, position.Longitude);
                AddParameter(command, "@speed", NpgsqlDbType.Integer, position.Speed);
                AddParameter(command, "@degree", NpgsqlDbType.Double, position.Degree);
                AddParameter(command, "@gps", NpgsqlDbType.Smallint, (short)(position.Gps ? 1 : 0));
                AddParameter(command, "@sat", NpgsqlDbType.Integer, position.Sat);
                AddParameter(command, "@ign", NpgsqlDbType.Smallint, (short)(position.Ign ? 1 : 0));
                AddParameter(command, "@block", NpgsqlDbType.Smallint, (short)(position.Block ? 1 : 0));
                AddParameter(command, "@io", NpgsqlDbType.Varchar, position.Io);
                AddParameter(command, "@bat_main", NpgsqlDbType.Double, position.BatMain);
                AddParameter(command, "@bat_back", NpgsqlDbType.Double, position.BatBack);
                AddParameter(command, "@storage", NpgsqlDbType.Smallint, (short)(position.Storage ? 1 : 0));
                AddParameter(command, "@msg_type_id", NpgsqlDbType.Integer, position.MsgTypeId);
                AddParameter(command, "@device_model_id", NpgsqlDbType.Integer, position.DeviceModelId);
                return command.ExecuteNonQuery() > 0;
            }, nameof(InsertPosition), position.DeviceId);

            CloseCircuit();
            return inserted;
        }
        catch (Exception ex)
        {
            OpenCircuit();
            _logger.LogError(ex, "PostgreSQL: falha ao inserir posicao para device {DeviceId}", position.DeviceId);
            return false;
        }
    }

    public long GetTotalPositionCount()
    {
        try
        {
            return ExecuteWithRetry(() =>
            {
                using var connection = OpenConnection();
                using var command = CreateCommand(connection, "SELECT count(*) FROM position");
                var result = command.ExecuteScalar();
                return result is null or DBNull ? 0L : Convert.ToInt64(result);
            }, nameof(GetTotalPositionCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL: falha ao consultar total da tabela position");
            return 0L;
        }
    }

    public void CleanupPositions()
    {
        // TimescaleDB gerencia retenção automaticamente; limpeza manual não é necessária.
    }

    public string? GetVehiclePlateByDeviceId(string deviceId)
    {
        // Tabela veiculos não existe no PostgreSQL.
        return null;
    }

    public bool EnsureVehicleTableAndInsert(PositionRecord position, string plate)
    {
        // Tabelas xx_* não existem no PostgreSQL.
        return false;
    }

    public IReadOnlyList<CommandRecord> GetCommands(bool includeStatus3)
    {
        const string sql = @"SELECT id, device_id, criado, atualizado, parametros, tipo_comando_id, status_comando_id
FROM comando
WHERE status_comando_id IN (@status1, @status4, @status3, @status5)
ORDER BY criado ASC";

        try
        {
            return ExecuteWithRetry(() =>
            {
                using var connection = OpenConnection();
                using var command = CreateCommand(connection, sql);
                AddParameter(command, "@status1", NpgsqlDbType.Integer, 1);
                AddParameter(command, "@status4", NpgsqlDbType.Integer, 4);
                AddParameter(command, "@status3", NpgsqlDbType.Integer, includeStatus3 ? 3 : 0);
                AddParameter(command, "@status5", NpgsqlDbType.Integer, 5);
                return ReadCommands(command);
            }, nameof(GetCommands));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL: falha ao consultar comandos pendentes");
            return Array.Empty<CommandRecord>();
        }
    }

    public IReadOnlyList<CommandRecord> GetCommandsByDeviceId(string deviceId, bool includeStatus3)
    {
        const string sql = @"SELECT id, device_id, criado, atualizado, parametros, tipo_comando_id, status_comando_id
FROM comando
WHERE device_id = @device_id AND status_comando_id IN (@status1, @status4, @status3, @status5)
ORDER BY criado ASC";

        try
        {
            if (!decimal.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDeviceId))
            {
                _logger.LogWarning("PostgreSQL: device_id invalido para consulta de comandos: {DeviceId}", deviceId);
                return Array.Empty<CommandRecord>();
            }

            return ExecuteWithRetry(() =>
            {
                using var connection = OpenConnection();
                using var command = CreateCommand(connection, sql);
                AddParameter(command, "@device_id", NpgsqlDbType.Numeric, parsedDeviceId);
                AddParameter(command, "@status1", NpgsqlDbType.Integer, 1);
                AddParameter(command, "@status4", NpgsqlDbType.Integer, 4);
                AddParameter(command, "@status3", NpgsqlDbType.Integer, includeStatus3 ? 3 : 0);
                AddParameter(command, "@status5", NpgsqlDbType.Integer, 5);
                return ReadCommands(command);
            }, nameof(GetCommandsByDeviceId), deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL: falha ao consultar comandos do device {DeviceId}", deviceId);
            return Array.Empty<CommandRecord>();
        }
    }

    public bool UpdateCommand(CommandRecord command)
    {
        const string sql = @"UPDATE comando SET device_id = @device_id, criado = @criado, atualizado = @atualizado, parametros = @parametros, tipo_comando_id = @tipo_comando_id, status_comando_id = @status_comando_id WHERE id = @id";

        try
        {
            if (!decimal.TryParse(command.DeviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDeviceId))
            {
                _logger.LogWarning("PostgreSQL: device_id invalido para atualizacao de comando {CommandId}: {DeviceId}", command.Id, command.DeviceId);
                return false;
            }

            return ExecuteWithRetry(() =>
            {
                using var connection = OpenConnection();
                using var dbCommand = CreateCommand(connection, sql);
                AddParameter(dbCommand, "@device_id", NpgsqlDbType.Numeric, parsedDeviceId);
                AddParameter(dbCommand, "@criado", NpgsqlDbType.Timestamp, ToDatabaseDateTime(command.CreatedAtUtc));
                AddParameter(dbCommand, "@atualizado", NpgsqlDbType.Timestamp, command.UpdatedAtUtc.HasValue ? ToDatabaseDateTime(command.UpdatedAtUtc.Value) : DBNull.Value);
                AddParameter(dbCommand, "@parametros", NpgsqlDbType.Varchar, command.Parameters);
                AddParameter(dbCommand, "@tipo_comando_id", NpgsqlDbType.Integer, command.CommandTypeId);
                AddParameter(dbCommand, "@status_comando_id", NpgsqlDbType.Integer, command.StatusCommandId);
                AddParameter(dbCommand, "@id", NpgsqlDbType.Integer, command.Id);
                return dbCommand.ExecuteNonQuery() > 0;
            }, nameof(UpdateCommand), command.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL: falha ao atualizar comando {CommandId}", command.Id);
            return false;
        }
    }

    public bool UpdateCommandStatusIfCurrent(CommandRecord command, int currentStatus, int nextStatus, DateTimeOffset updatedAtUtc)
    {
        const string sql = "UPDATE comando SET atualizado = @atualizado, status_comando_id = @next_status WHERE id = @id AND status_comando_id = @current_status";

        try
        {
            return ExecuteWithRetry(() =>
            {
                using var connection = OpenConnection();
                using var dbCommand = CreateCommand(connection, sql);
                AddParameter(dbCommand, "@atualizado", NpgsqlDbType.Timestamp, ToDatabaseDateTime(updatedAtUtc));
                AddParameter(dbCommand, "@next_status", NpgsqlDbType.Integer, nextStatus);
                AddParameter(dbCommand, "@id", NpgsqlDbType.Integer, command.Id);
                AddParameter(dbCommand, "@current_status", NpgsqlDbType.Integer, currentStatus);
                return dbCommand.ExecuteNonQuery() > 0;
            }, nameof(UpdateCommandStatusIfCurrent), command.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL: falha ao atualizar status do comando {CommandId}", command.Id);
            return false;
        }
    }

    private NpgsqlConnection OpenConnection()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _commandTimeoutSeconds;
        return command;
    }

    private T ExecuteWithRetry<T>(Func<T> operation, string operationName, string? deviceId = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (attempt < _maxRetryAttempts && IsTransient(ex))
            {
                var delay = ComputeRetryDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "PostgreSQL: falha transitória em {Operation} (tentativa {Attempt}/{MaxAttempts}){DeviceLabel}. Nova tentativa em {DelayMs}ms",
                    operationName,
                    attempt,
                    _maxRetryAttempts,
                    string.IsNullOrWhiteSpace(deviceId) ? string.Empty : $" para device {deviceId}",
                    delay.TotalMilliseconds);
                Thread.Sleep(delay);
            }
        }
    }

    private TimeSpan ComputeRetryDelay(int attempt)
    {
        var exponential = _retryBaseDelayMilliseconds * Math.Pow(2, Math.Max(0, attempt - 1));
        var jitter = Random.Shared.Next(0, _retryBaseDelayMilliseconds + 1);
        var totalMilliseconds = Math.Min(_retryMaxDelayMilliseconds, exponential + jitter);
        return TimeSpan.FromMilliseconds(totalMilliseconds);
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is TimeoutException or IOException or SocketException)
        {
            return true;
        }

        if (ex is NpgsqlException npgsqlException)
        {
            return npgsqlException.IsTransient;
        }

        return false;
    }

    private bool IsCircuitOpen()
    {
        var openUntilUnixMs = Interlocked.Read(ref _circuitOpenUntilUnixMs);
        if (openUntilUnixMs <= 0)
        {
            return false;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < openUntilUnixMs;
    }

    private void OpenCircuit()
    {
        var openUntil = DateTimeOffset.UtcNow.AddSeconds(_circuitOpenSeconds).ToUnixTimeMilliseconds();
        Interlocked.Exchange(ref _circuitOpenUntilUnixMs, openUntil);
    }

    private void CloseCircuit()
    {
        Interlocked.Exchange(ref _circuitOpenUntilUnixMs, 0);
    }

    private static IReadOnlyList<CommandRecord> ReadCommands(NpgsqlCommand command)
    {
        var commands = new List<CommandRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            commands.Add(new CommandRecord
            {
                Id = reader.GetInt32(0),
                DeviceId = Convert.ToString(reader.GetValue(1)) ?? string.Empty,
                CreatedAtUtc = ReadDateTimeOffset(reader, 2),
                UpdatedAtUtc = reader.IsDBNull(3) ? null : ReadDateTimeOffset(reader, 3),
                Parameters = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                CommandTypeId = reader.GetInt32(5),
                StatusCommandId = reader.GetInt32(6)
            });
        }

        return commands;
    }

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var dateTime = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), BrazilOffset).ToUniversalTime();
    }

    private static DateTime ToDatabaseDateTime(DateTimeOffset value)
    {
        return value.ToOffset(BrazilOffset).DateTime;
    }

    private static void AddParameter(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
    }
}

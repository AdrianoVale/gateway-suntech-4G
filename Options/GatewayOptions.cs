using System.ComponentModel.DataAnnotations;

namespace GatewaySunteh4G_NET8.Options;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    [Required]
    public string ProjectLabel { get; set; } = "gateway_sunteh_4g_net8";

    [Required]
    public UdpListenerOptions Udp { get; set; } = new();

    [Required]
    public DeviceRegistryOptions Devices { get; set; } = new();

    [Required]
    public MetricsOptions Metrics { get; set; } = new();

    [Required]
    public DatabaseOptions Database { get; set; } = new();

    [Required]
    public CommandProcessingOptions Commands { get; set; } = new();

    [Required]
    public ReplayOptions Replay { get; set; } = new();

    [Required]
    public PositionMaintenanceOptions PositionMaintenance { get; set; } = new();

    [Required]
    public PostgresDatabaseOptions PostgresDatabase { get; set; } = new();

    [Required]
    public FileLoggingOptions FileLogging { get; set; } = new();

    public HubOptions Hub { get; set; } = new();
}

public sealed class UdpListenerOptions
{
    [Range(1, 65535)]
    public int Port { get; set; } = 9040;

    [Range(65536, 16777216)]
    public int ReceiveBufferBytes { get; set; } = 4 * 1024 * 1024;

    [Range(512, 65535)]
    public int PayloadBufferBytes { get; set; } = 2048;

    [Range(1, 256)]
    public int WorkerCount { get; set; } = 16;

    [Range(1, 500000)]
    public int QueueCapacity { get; set; } = 8192;

    [Range(1000, 60000)]
    public int ReceiverBackoffMilliseconds { get; set; } = 5000;
}

public sealed class DeviceRegistryOptions
{
    [Range(30, 86400)]
    public int InactiveAfterSeconds { get; set; } = 300;

    [Range(5, 3600)]
    public int CleanupIntervalSeconds { get; set; } = 60;
}

public sealed class MetricsOptions
{
    [Required]
    public string Url { get; set; } = "http://0.0.0.0:9054";
}

public sealed class DatabaseOptions
{
    [Required]
    public string ConnectionString { get; set; } = "Server=127.0.0.1;Database=localize;User ID=localizeuser;Password=localizepa2022;";
}

public sealed class CommandProcessingOptions
{
    [Range(1, 3600)]
    public int PollIntervalMilliseconds { get; set; } = 1000;

    [Range(1, 128)]
    public int WorkerCount { get; set; } = 3;

    [Range(1000, 600000)]
    public int RetryAfterMilliseconds { get; set; } = 10000;
}

public sealed class ReplayOptions
{
    [Required]
    public string CacheDirectory { get; set; } = "log/cache";

    [Range(1, 3600)]
    public int CheckIntervalSeconds { get; set; } = 30;

    [Range(1, 3600)]
    public int DbRetryIntervalSeconds { get; set; } = 15;
}

public sealed class PositionMaintenanceOptions
{
    [Range(10, 86400)]
    public int CheckIntervalSeconds { get; set; } = 120;

    [Range(1, long.MaxValue)]
    public long CleanupThreshold { get; set; } = 15000;
}

public sealed class PostgresDatabaseOptions
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [Range(1, 120)]
    public int ConnectTimeoutSeconds { get; set; } = 5;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 10;

    [Range(0, 300)]
    public int KeepAliveSeconds { get; set; } = 30;

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(50, 10000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    [Range(100, 30000)]
    public int RetryMaxDelayMilliseconds { get; set; } = 2000;

    [Range(1, 300)]
    public int CircuitOpenSeconds { get; set; } = 15;

    [Range(100, 60000)]
    public int PollIntervalMilliseconds { get; set; } = 1000;

    [Range(1, 128)]
    public int WorkerCount { get; set; } = 3;
}

public sealed class FileLoggingOptions
{
    [Required]
    public string Directory { get; set; } = "log";

    [Required]
    public string CurrentFileName { get; set; } = "gateway-atual.log";

    [Required]
    public string ArchivedDirectoryName { get; set; } = "arquivados";

    [Required]
    public string ArchiveFilePrefix { get; set; } = "gateway";

    [Range(1, 30)]
    public int RetentionDays { get; set; } = 3;
}

public sealed class HubOptions
{
    /// <summary>Habilita o SignalR Hub e push de posições em tempo real.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>URL interna em que o Hub escuta (não expor diretamente — use Apache proxy).</summary>
    [Required]
    public string Url { get; set; } = "http://127.0.0.1:9055";

    /// <summary>Segredo HS256 compartilhado com o PHP. Mínimo 32 caracteres.</summary>
    public string JwtSecret { get; set; } = string.Empty;

    [Required]
    public string JwtIssuer { get; set; } = "blt-php";

    [Required]
    public string JwtAudience { get; set; } = "gateway-pos";

    /// <summary>Tempo de cache de permissões por userId, em minutos.</summary>
    [Range(1, 60)]
    public int PermissionCacheMinutes { get; set; } = 5;
}
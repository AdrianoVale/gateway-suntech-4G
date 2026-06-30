using System.Globalization;
using System.Text;
using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services.Models;
using Microsoft.Extensions.Options;

namespace GatewaySunteh4G_NET8.Services;

public sealed class DiskCacheStore : IDiskCacheStore
{
    private const string Separator = "|";
    private const string NullValue = "NULL";
    private readonly object _writeLock = new();
    private readonly DirectoryInfo _directory;
    private readonly ILogger<DiskCacheStore> _logger;

    public DiskCacheStore(ILogger<DiskCacheStore> logger, IOptions<GatewayOptions> options)
    {
        _logger = logger;
        var path = Path.GetFullPath(options.Value.Replay.CacheDirectory, AppContext.BaseDirectory);
        _directory = Directory.CreateDirectory(path);
    }

    public void Save(PositionRecord position, string? plate)
    {
        try
        {
            var filename = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}.pos";
            var file = new FileInfo(Path.Combine(_directory.FullName, filename));
            var payload = string.Join(Separator, new[]
            {
                position.DeviceId,
                position.DatetimeUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                position.Latitude.ToString(CultureInfo.InvariantCulture),
                position.Longitude.ToString(CultureInfo.InvariantCulture),
                position.Speed.ToString(CultureInfo.InvariantCulture),
                position.Degree.ToString(CultureInfo.InvariantCulture),
                position.Gps.ToString(),
                position.Sat.ToString(CultureInfo.InvariantCulture),
                position.Ign.ToString(),
                position.Block.ToString(),
                Escape(position.Io),
                position.BatMain.ToString(CultureInfo.InvariantCulture),
                position.BatBack.ToString(CultureInfo.InvariantCulture),
                position.Storage.ToString(),
                position.MsgTypeId.ToString(CultureInfo.InvariantCulture),
                position.DeviceModelId.ToString(CultureInfo.InvariantCulture),
                Escape(plate)
            });

            lock (_writeLock)
            {
                File.WriteAllText(file.FullName, payload, Encoding.UTF8);
            }

            _logger.LogWarning("CACHE_DISCO: posição salva em cache -> {FileName}", file.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao salvar posição no cache em disco");
        }
    }

    public IReadOnlyList<CachedPositionEntry> LoadPending()
    {
        var files = _directory.GetFiles("*.pos").OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
        var entries = new List<CachedPositionEntry>(files.Length);
        foreach (var file in files)
        {
            try
            {
                var line = File.ReadAllText(file.FullName, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(Separator, StringSplitOptions.None);
                if (parts.Length < 17)
                {
                    continue;
                }

                entries.Add(new CachedPositionEntry
                {
                    File = file,
                    Plate = Unescape(parts[16]),
                    Position = new PositionRecord
                    {
                        DeviceId = parts[0],
                        DatetimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[1], CultureInfo.InvariantCulture)),
                        Latitude = double.Parse(parts[2], CultureInfo.InvariantCulture),
                        Longitude = double.Parse(parts[3], CultureInfo.InvariantCulture),
                        Speed = int.Parse(parts[4], CultureInfo.InvariantCulture),
                        Degree = double.Parse(parts[5], CultureInfo.InvariantCulture),
                        Gps = bool.Parse(parts[6]),
                        Sat = int.Parse(parts[7], CultureInfo.InvariantCulture),
                        Ign = bool.Parse(parts[8]),
                        Block = bool.Parse(parts[9]),
                        Io = Unescape(parts[10]) ?? string.Empty,
                        BatMain = double.Parse(parts[11], CultureInfo.InvariantCulture),
                        BatBack = double.Parse(parts[12], CultureInfo.InvariantCulture),
                        Storage = bool.Parse(parts[13]),
                        MsgTypeId = int.Parse(parts[14], CultureInfo.InvariantCulture),
                        DeviceModelId = int.Parse(parts[15], CultureInfo.InvariantCulture)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao ler entrada de cache {FileName}", file.Name);
            }
        }

        return entries;
    }

    public void Remove(FileInfo file)
    {
        if (file.Exists)
        {
            file.Delete();
        }
    }

    public int PendingCount()
    {
        return _directory.GetFiles("*.pos").Length;
    }

    private static string Escape(string? value)
    {
        return value is null ? NullValue : value.Replace("|", "§", StringComparison.Ordinal);
    }

    private static string? Unescape(string value)
    {
        return value == NullValue ? null : value.Replace("§", "|", StringComparison.Ordinal);
    }
}
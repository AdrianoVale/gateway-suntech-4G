using GatewaySunteh4G_NET8.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GatewaySunteh4G_NET8.Services;

public sealed class PermissionQueryService : IPermissionQueryService
{
    // Consulta idêntica à PHP buscaVeiculosVisiveisUsuario:
    // Primary path (por grupo), fallback (por cliente).
    private const string Sql = @"
        SELECT DISTINCT CAST(v.equipamento AS text)
          FROM veiculos v
          JOIN veiculos_grupos_clientes vgc ON vgc.id_veiculo = v.id
          JOIN grupos_clientes gc           ON gc.id = vgc.id_grupo
          JOIN usersitecliente_grupos ug    ON ug.id_grupo = gc.id
         WHERE ug.id_usuario = @userId
           AND v.equipamento IS NOT NULL
           AND v.equipamento > 0
           AND COALESCE(v.servico, '') <> 'Remocao'
         UNION
        SELECT DISTINCT CAST(v.equipamento AS text)
          FROM veiculos v
         WHERE v.cliente = @clientId
           AND v.equipamento IS NOT NULL
           AND v.equipamento > 0
           AND COALESCE(v.servico, '') <> 'Remocao'";

    private readonly string _connectionString;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheTtl;
    private readonly ILogger<PermissionQueryService> _logger;

    public PermissionQueryService(
        IOptions<GatewayOptions> options,
        IMemoryCache cache,
        ILogger<PermissionQueryService> logger)
    {
        _connectionString = options.Value.PostgresDatabase.ConnectionString;
        _cache            = cache;
        _cacheTtl         = TimeSpan.FromMinutes(options.Value.Hub.PermissionCacheMinutes);
        _logger           = logger;
    }

    public async Task<IReadOnlyList<string>> GetAuthorizedDeviceIdsAsync(string userId, string clientId)
    {
        var cacheKey = $"perm_{userId}_{clientId}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached != null)
            return cached;

        var list = new List<string>();
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = Sql;
            cmd.Parameters.AddWithValue("userId",   int.Parse(userId));
            cmd.Parameters.AddWithValue("clientId", int.Parse(clientId));
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var eq = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(eq))
                    list.Add(eq.Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PermissionQueryService: erro ao consultar device_ids para userId={UserId}", userId);
        }

        var result = (IReadOnlyList<string>)list.AsReadOnly();
        _cache.Set(cacheKey, result, _cacheTtl);
        return result;
    }
}

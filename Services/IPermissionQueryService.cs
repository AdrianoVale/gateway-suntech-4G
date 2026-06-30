namespace GatewaySunteh4G_NET8.Services;

public interface IPermissionQueryService
{
    Task<IReadOnlyList<string>> GetAuthorizedDeviceIdsAsync(string userId, string clientId);
}

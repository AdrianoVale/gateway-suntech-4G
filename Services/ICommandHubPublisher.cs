namespace GatewaySunteh4G_NET8.Services;

public interface ICommandHubPublisher
{
    Task PublishAsync(string deviceId, int commandId, int statusId);
}

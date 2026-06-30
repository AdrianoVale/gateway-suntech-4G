using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public interface IPositionHubPublisher
{
    Task PublishAsync(string deviceId, PositionRecord position);
}

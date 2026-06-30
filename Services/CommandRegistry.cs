using System.Collections.Concurrent;
using GatewaySunteh4G_NET8.Services.Models;

namespace GatewaySunteh4G_NET8.Services;

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly ConcurrentDictionary<string, PendingCommand> _commands = new(StringComparer.Ordinal);

    public int Count => _commands.Count;

    public void Upsert(PendingCommand command)
    {
        _commands[command.Command.DeviceId] = command;
    }

    public bool TryGet(string deviceId, out PendingCommand? command)
    {
        return _commands.TryGetValue(deviceId, out command);
    }

    public PendingCommand? Complete(string deviceId)
    {
        return _commands.TryRemove(deviceId, out var command) ? command : null;
    }

    public int RemoveInactive(TimeSpan maxAge, DateTimeOffset nowUtc)
    {
        var removed = 0;
        foreach (var pair in _commands)
        {
            if (nowUtc - pair.Value.UpdatedAtUtc < maxAge)
            {
                continue;
            }

            if (_commands.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }
}
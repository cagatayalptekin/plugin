// Infrastructure/OrderStream.cs
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PluginTest.Infrastructure;

public sealed class OrderStream
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subs = new();

    public (Guid id, ChannelReader<string> reader) Subscribe(int capacity = 256)
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var id = Guid.NewGuid();
        _subs.TryAdd(id, ch);
        return (id, ch.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subs.TryRemove(id, out var ch))
            ch.Writer.TryComplete();
    }

    public void Publish(string message)
    {
        foreach (var ch in _subs.Values)
            ch.Writer.TryWrite(message);
    }
}

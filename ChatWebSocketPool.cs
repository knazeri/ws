using System.Collections.Concurrent;
using System.Net.WebSockets;
using WebSocketsSample.Core;

namespace WebSocketsSample;

/// <summary>
/// Represents a factory for creating <see cref="ChatWebSocketPool"/> instances.
/// </summary>
public interface IChatWebSocketPoolFactory : IDisposable
{
    ChatWebSocketPool CreateOrGet(string name);
}


/// <summary>
/// Creates and caches <see cref="ChatWebSocketPool"/> instances.
/// When disposed, all cached instances are disposed.
/// </summary>
public class ChatWebSocketPoolFactory : IChatWebSocketPoolFactory
{
    readonly ConcurrentDictionary<string, ChatWebSocketPool> _pools = new();

    public ChatWebSocketPool CreateOrGet(string name)
    {
        return _pools.GetOrAdd(name, t => new ChatWebSocketPool(t));
    }

    public void Dispose()
    {
        foreach (var pool in _pools.Values)
        {
            pool.Dispose();
        }

        _pools.Clear();
    }
}


/// <summary>
/// Represents a chat <see cref="WebSocketPool"/> implementation.
/// </summary>
public class ChatWebSocketPool : WebSocketPool
{
    public ChatWebSocketPool(string name)
        : base(name)
    {
        // initialize custom code
    }

    protected override void OnAdd(IWebSocketPoolItem item)
    {
        // custom logic when a socket added
        base.OnAdd(item);
    }

    protected override void OnRemove(IWebSocketPoolItem item, WebSocketPoolItemResult result)
    {
        // custom logic when a socket removed
        base.OnRemove(item, result);
    }
}

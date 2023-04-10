using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace WebSocketsSample.Core;

/// <summary>
/// Represents a WebSocket pool.
/// A WebSocket is added to or removed from a pool by a unique id.
/// </summary>
public interface IWebSocketPool : IReadOnlyCollection<IWebSocketPoolItem>, IDisposable
{
    /// <summary>
    /// Name of the pool
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Tries adding a WebSocket to the pool.
    /// Returns true if the WebSocket is added to the pool; otherwise, false.
    /// When the method returns, contains the WebSocketPoolItem instance associated with the specified id.
    /// </summary>
    /// <param name="socket">An open WebSocket instance</param>
    /// <param name="id">Unique id of the socket</param>
    /// <param name="item">When this method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found; otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    bool TryAdd(string id, WebSocket socket, out IWebSocketPoolItem item);

    /// <summary>
    /// Tries removing a WebSocket from the pool. If the WebSocket is not in the pool, the method returns false.
    /// When the method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found.
    /// </summary>
    /// <param name="id">Unique id of the socket</param>
    /// <param name="item">When this method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found; otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    bool TryRemove(string id, out IWebSocketPoolItem item);

    /// <summary>
    /// Gets a WebSocket from the pool. If the WebSocket is not in the pool, the method returns false.
    /// </summary>
    /// <param name="id">Unique id of the socket</param>
    /// <param name="item">When this method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found; otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    bool TryGet(string id, out IWebSocketPoolItem item);

    /// <summary>
    /// Sends a message to all WebSockets in the pool.
    /// </summary>
    /// <param name="buffer">The buffer to be sent over the connection.</param>
    /// <param name="messageType">Indicates whether the application is sending a binary or text message.</param>
    /// <param name="endOfMessage">Indicates whether the data in "buffer" is the last part of a message.</param>
    /// <param name="cancellationToken">The token that propagates the notification that operations should be canceled.</param>
    Task SendAllAsync(Memory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to a WebSocket in the pool.
    /// </summary>
    /// <param name="id">Id of the WebSocket</param>
    /// <param name="buffer">The buffer to be sent over the connection.</param>
    /// <param name="messageType">Indicates whether the application is sending a binary or text message.</param>
    /// <param name="endOfMessage">Indicates whether the data in "buffer" is the last part of a message.</param>
    /// <param name="cancellationToken">The token that propagates the notification that operations should be canceled.</param>
    Task<WebSocketPoolItemResult> SendAsync(string id, Memory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts receiving messages from a WebSocket in the pool.
    /// </summary>
    /// <param name="id">Id of the WebSocket</param>
    /// <param name="buffer">The buffer to be receive from the connection.</param>
    /// <param name="receiveHandler">A callback that is invoked when a message is received.</param>
    /// <param name="cancellationToken">The token that propagates the notification that operations should be canceled.</param>
    Task<WebSocketPoolItemResult> ReceiveAsync(string id, Memory<byte> buffer, Action<ValueWebSocketReceiveResult, Memory<byte>> receiveHandler, CancellationToken cancellationToken);
}


/// <summary>
/// Represents a base class for WebSocketPool.
/// </summary>
public abstract class WebSocketPool : IWebSocketPool, IDisposable
{
    readonly TimeSpan _cleanUpInterval;
    readonly CancellationTokenSource _disposeToken = new();
    readonly ConcurrentDictionary<string, WebSocketPoolItem> _sockets = new();


    /// <summary>
    /// Creates a new WebSocketPool.
    /// </summary>
    /// <param name="name">Name of the pool</param>
    /// <param name="cleanUpInterval">Optional cleanup timer used to remove closed WebSockets from the pool; The default is <code>WebSocket.DefaultKeepAliveInterval</code></param>
    public WebSocketPool(string name, TimeSpan cleanUpInterval = default)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _cleanUpInterval = cleanUpInterval == default ? WebSocket.DefaultKeepAliveInterval : cleanUpInterval;

        // Fire and forget cleanup task
        Task.Factory.StartNew(CleanUp, TaskCreationOptions.LongRunning);
    }


    /// <summary>
    /// Returns the number of WebSockets in the pool.
    /// </summary>
    public virtual int Count => _sockets.Count;


    /// <summary>
    /// Name of the pool
    /// </summary>
    public string Name { get; }


    /// <summary>
    /// Try adding a WebSocket to the pool.
    /// Returns true if the WebSocket is added to the pool; otherwise, false.
    /// When the method returns, contains the WebSocketPoolItem instance associated with the specified id.
    /// </summary>
    /// <param name="id">Unique id of the socket</param>
    /// <param name="socket">An open WebSocket instance</param>
    /// <param name="item">When this method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found; otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    public bool TryAdd(string id, WebSocket socket, out IWebSocketPoolItem item)
    {
        if (_disposeToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Pool is disposed!");
        }

        if (_sockets.TryAdd(id, new WebSocketPoolItem(this, socket, id, new())))
        {
            item = _sockets[id];
            OnAdd(item);
            return true;
        }

        item = default!;
        return false;
    }


    /// <summary>
    /// Called when a WebSocket is added to the pool.
    /// </summary>
    /// <param name="item">An item that was added to the pool</param>
    protected virtual void OnAdd(IWebSocketPoolItem item) { }


    /// <summary>
    /// Tries removing a WebSocket from the pool. If the WebSocket is not in the pool, the method returns false.
    /// When the method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found.
    /// </summary>
    /// <param name="id">Unique id of the socket</param>
    /// <param name="item">When this method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found; otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    public bool TryRemove(string id, out IWebSocketPoolItem item)
    {
        return Remove(id, WebSocketPoolItemResult.Removed, out item);
    }


    /// <summary>
    /// Called when a WebSocket is removed from the pool.
    /// </summary>
    /// <param name="item">An item that was removed to the pool</param>
    /// <param name="result">The result of item that was removed</param>
    protected virtual void OnRemove(IWebSocketPoolItem item, WebSocketPoolItemResult result) { }


    /// <summary>
    /// Gets a WebSocket associated with the specified id from the pool. If the WebSocket is not found, the method returns false.
    /// </summary>
    /// <param name="id">Unique id of the socket</param>
    /// <param name="item">When this method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found; otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    public bool TryGet(string id, out IWebSocketPoolItem item)
    {
        // this operation does not require check for dispose token

        if (_sockets.TryGetValue(id, out var val))
        {
            item = val;
            return true;
        }

        item = default!;
        return false;
    }


    /// <summary>
    /// Sends a message to all WebSockets in the pool.
    /// </summary>
    /// <param name="buffer">The buffer to be sent over the connection.</param>
    /// <param name="messageType">Indicates whether the application is sending a binary or text message.</param>
    /// <param name="endOfMessage">Indicates whether the data in "buffer" is the last part of a message.</param>
    /// <param name="cancellationToken">The token that propagates the notification that operations should be canceled.</param>
    public virtual async Task SendAllAsync(Memory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken = default)
    {
        var tasks = _sockets.Keys.Select(t => SendAsync(t, buffer, messageType, endOfMessage, cancellationToken));

        await Task.WhenAll(tasks).WaitAsync(_disposeToken.Token);
    }


    /// <summary>
    /// Sends a message to a WebSocket in the pool.
    /// </summary>
    /// <param name="id">Id of the WebSocket</param>
    /// <param name="buffer">The buffer to be sent over the connection.</param>
    /// <param name="messageType">Indicates whether the application is sending a binary or text message.</param>
    /// <param name="endOfMessage">Indicates whether the data in "buffer" is the last part of a message.</param>
    /// <param name="cancellationToken">The token that propagates the notification that operations should be canceled.</param>
    public virtual async Task<WebSocketPoolItemResult> SendAsync(string id, Memory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken = default)
    {
        var res = WebSocketPoolItemResult.None;

        if (_sockets.TryGetValue(id, out var item) && !_disposeToken.IsCancellationRequested)
        {
            if (item.IsConnected)
            {
                try
                {
                    res = WebSocketPoolItemResult.Normal;
                    await item.WebSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
                }
                catch
                {
                    res = WebSocketPoolItemResult.Aborted;
                    Remove(id, res, out _);
                }
            }
            else
            {
                res = WebSocketPoolItemResult.Aborted;
                Remove(id, res, out _);
            }
        }

        return res;
    }


    /// <summary>
    /// Starts receiving messages from a WebSocket in the pool.
    /// </summary>
    /// <param name="id">Id of the WebSocket</param>
    /// <param name="buffer">The buffer to be receive from the connection.</param>
    /// <param name="receiveHandler">A callback that is invoked when a message is received.</param>
    /// <param name="cancellationToken">The token that propagates the notification that operations should be canceled.</param>
    public virtual async Task<WebSocketPoolItemResult> ReceiveAsync(string id, Memory<byte> buffer, Action<ValueWebSocketReceiveResult, Memory<byte>> receiveHandler, CancellationToken cancellationToken)
    {
        var res = WebSocketPoolItemResult.None;

        if (_sockets.TryGetValue(id, out var item))
        {
            try
            {
                res = WebSocketPoolItemResult.Aborted;
                while (item.IsConnected)
                {
                    var result = await item.WebSocket.ReceiveAsync(buffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        res = WebSocketPoolItemResult.ClosedByClient;
                        Remove(item.Id, res, out _);
                    }

                    // Fire and forget the receive handler
                    _ = Task.Run(() => receiveHandler(result, buffer.Slice(0, result.Count)), cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                Remove(item.Id, res, out _);
            }
        }

        return res;
    }


    /// <summary>
    /// Disposes the pool and returns all sockets.
    /// </summary>
    public void Dispose()
    {
        if (!_disposeToken.IsCancellationRequested)
        {
            _disposeToken.Cancel();

            foreach (var id in _sockets.Keys)
            {
                Remove(id, WebSocketPoolItemResult.Removed, out _);
            }
        }
    }


    /// <summary>
    /// Removes a WebSocket from the pool. If the WebSocket is not in the pool, the method returns false.
    /// When the method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found.
    /// </summary>
    /// <param name="id">Unique id of the socket</param>
    /// <param name="result">The result of the remove operation</param>
    /// <param name="item">When this method returns, contains the WebSocketPoolItem instance associated with the specified id, if the id is found; otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    bool Remove(string id, WebSocketPoolItemResult result, out IWebSocketPoolItem item)
    {
        if (_sockets.Remove(id, out var val))
        {
            item = val;
            val.TaskCompletionSource.TrySetResult(result);    // will resume the task
            OnRemove(item, result);
            return true;
        }

        item = default!;
        return false;
    }


    /// <summary>
    /// Runs the clean-up procedure. The interval for which this process runs is specified in the constructor.
    /// </summary>
    async Task CleanUp()
    {
        try
        {
            while (!_disposeToken.IsCancellationRequested)
            {
                // requires the dispose token to be canceled
                await Task.Delay(_cleanUpInterval, _disposeToken.Token);

                foreach (var item in _sockets.Values)
                {
                    if (!item.IsConnected)
                    {
                        // Remove the sockets from the pool. This is a safe operation.
                        Remove(item.Id, WebSocketPoolItemResult.Aborted, out _);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancel exception; thrown by Task.Delay
        }
    }


    IEnumerator<IWebSocketPoolItem> IEnumerable<IWebSocketPoolItem>.GetEnumerator() => _sockets.Values.GetEnumerator();


    IEnumerator IEnumerable.GetEnumerator() => _sockets.Values.GetEnumerator();


    /// <summary>
    /// Represents a named WebSocket item in a WebSocketPool.
    /// The TaskCompletionSource gets resolved when the socket is removed from the pool.
    /// </summary>
    record WebSocketPoolItem(WebSocketPool Pool, WebSocket WebSocket, string Id, TaskCompletionSource<WebSocketPoolItemResult> TaskCompletionSource) : IWebSocketPoolItem
    {
        public bool IsConnected
            => WebSocket?.CloseStatus.HasValue is false && WebSocket?.State is WebSocketState.Open;

        public Task<WebSocketPoolItemResult> WaitAsync(CancellationToken cancellationToken = default)
            => TaskCompletionSource.Task.WaitAsync(cancellationToken);

        public Task<WebSocketPoolItemResult> ReceiveAsync(Memory<byte> buffer, Action<ValueWebSocketReceiveResult, Memory<byte>> receiveHandler, CancellationToken cancellationToken)
            => Pool.ReceiveAsync(Id, buffer, receiveHandler, cancellationToken);
    }
}

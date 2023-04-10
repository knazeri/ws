using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace WebSocketsSample.Core;

/// <summary>
/// Represents a named WebSocket item in a WebSocketPool.
/// </summary>
public interface IWebSocketPoolItem
{
    /// <summary>
    /// Id of the WebSocket
    /// </summary>
    string Id { get; }

    /// <summary>
    /// A WebSocket instance
    /// </summary>
    WebSocket WebSocket { get; }

    /// <summary>
    /// Returns true if the WebSocket is connected; otherwise, false.
    /// </summary>
    bool IsConnected => WebSocket?.CloseStatus.HasValue is false && WebSocket.State is WebSocketState.Open;

    /// <summary>
    /// Returns a task that completes when the WebSocket is closed or removed from the pool.
    /// <param name="cancellationToken">The token that propagates the notification that operations should be canceled.</param>
    /// </summary>
    Task<WebSocketPoolItemResult> WaitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Receives data from the WebSocket connection asynchronously.
    /// Returns a task that completes when the WebSocket is closed or removed from the pool.
    /// </summary>
    /// <param name="buffer">The application buffer that is the storage location for the received data.</param>
    /// <param name="cancellationToken">The cancellation token to use to cancel the receive operation.</param>
    Task<WebSocketPoolItemResult> ReceiveAsync(Memory<byte> buffer, Action<ValueWebSocketReceiveResult, Memory<byte>> receiveHandler, CancellationToken cancellationToken);
}


/// <summary>
/// Represents the result of wait/receive a WebSocket in the pool.
/// </summary>
public enum WebSocketPoolItemResult
{
    /// <summary>
    /// Unknown result state.
    /// </summary>
    None = 0,

    /// <summary>
    /// Normal state.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Removed from the pool.
    /// </summary>
    Removed = 2,

    /// <summary>
    /// Connection aborted.
    /// </summary>
    Aborted = 4,

    /// <summary>
    /// Closed by the client.
    /// </summary>
    ClosedByClient = 8,
}
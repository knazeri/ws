using System;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc;
using WebSocketsSample.Core;

namespace WebSocketsSample;

public class ChatWebSocketController : ControllerBase
{
    readonly IChatWebSocketPoolFactory _chatPoolFactory;

    public ChatWebSocketController(IChatWebSocketPoolFactory customWebSocketPoolProvider)
    {
        _chatPoolFactory = customWebSocketPoolProvider;
    }

    [Route("/chat/{roomname}")]
    public async Task Connect([FromRoute] string roomname, [FromQuery] string nickname, CancellationToken cancellationToken = default)
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var pool = _chatPoolFactory.CreateOrGet(roomname);

            // add the socket to the pool
            if (pool.TryAdd(nickname, webSocket, out var item))
            {
                await ProcessWebSocketAsync(pool, item, cancellationToken);

                // normal close: closed by the client or removed from the pool
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
            }
            else
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Duplicate name", cancellationToken);
            }
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }


    /// <summary>
    /// Prepares a WebSocket in the pool to send/receive messages
    /// </summary>
    async Task ProcessWebSocketAsync(IWebSocketPool pool, IWebSocketPoolItem item, CancellationToken cancellationToken)
    {
        // wait in the pool to receive a message from client and send it to all other sockets in the same pool
        var receiveTask = item.ReceiveAsync(new byte[1024 * 4], async (res, data) =>
        {
            if (res.MessageType == WebSocketMessageType.Text)
            {
                // relay the message to all other sockets in the same pool
                await pool.SendAllAsync(data.ToArray(), WebSocketMessageType.Text, true, cancellationToken);
            }
        }, cancellationToken);

        // wait in the pool for to receive messages from other clients in the same pool
        var waitTask = item.WaitAsync(cancellationToken);

        // wait for either of tasks: socket is closed or removed from the pool
        await Task.WhenAny(receiveTask, waitTask);
    }
}
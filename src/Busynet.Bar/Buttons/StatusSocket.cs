using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace Busynet.Bar.Buttons;

/// <summary>The bar's status WebSocket: binary protobuf state frames, carrying button events among others.</summary>
public static class StatusSocket
{
    private const int MaxMessageBytes = 1024 * 1024;

    // Same as the Python `websockets` defaults busyboy relies on: a connection that silently died
    // (Wi-Fi drop, laptop sleep) is noticed within ~40 s rather than never.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(20);

    private static readonly ReadOnlyMemory<byte> EnableMessage = "{\"enable\": true}"u8.ToArray();

    /// <summary>
    /// The socket URI, with the token in its query string when one is set. It is a secret: never log it.
    /// </summary>
    public static Uri BuildUri(BarConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.Token is null
            ? connection.StatusSocketUri
            : new UriBuilder(connection.StatusSocketUri) { Query = "x-api-token=" + Uri.EscapeDataString(connection.Token) }.Uri;
    }

    /// <summary>
    /// Connects, asks the bar to stream its state, and yields each complete binary message. Text messages
    /// are ignored. Ends when the server closes the socket; connection failures throw.
    /// </summary>
    public static async IAsyncEnumerable<byte[]> ReadFramesAsync(
        BarConnection connection,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = KeepAliveInterval;
        socket.Options.KeepAliveTimeout = KeepAliveTimeout;

        await socket.ConnectAsync(BuildUri(connection), ct).ConfigureAwait(false);
        await socket.SendAsync(EnableMessage, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);

        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                yield break;
            }

            if (message.Length + result.Count > MaxMessageBytes)
            {
                throw new InvalidDataException("Status message too large.");
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                yield return message.ToArray();
            }

            message.SetLength(0);
        }
    }
}

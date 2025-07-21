using System.Security.Cryptography;
using System.Text;
using Wired.IO.Http11.Context;
using Wired.IO.Protocol;

namespace Wired.IO.Http11;

public partial class WiredHttp11<TContext, TRequest>
{
    private static readonly ReadOnlyMemory<byte> WebsocketHandshakePrefix
        = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "u8.ToArray();

    private static readonly ReadOnlyMemory<byte> WebsocketHandshakeSuffix = "\r\n\r\n"u8.ToArray();

    /// <summary>
    /// Creates a WebSocket handshake response for an incoming WebSocket upgrade request.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="request">
    /// The raw HTTP request string received from the client, which includes headers and the WebSocket key.
    /// </param>
    /// <returns>
    /// A <see cref="string"/> representing the HTTP response to complete the WebSocket handshake,
    /// containing the necessary headers to switch protocols.
    /// </returns>
    /// <remarks>
    /// This method processes the WebSocket upgrade request by:
    /// - Extracting the `Sec-WebSocket-Key` header from the request.
    /// - Generating the `Sec-WebSocket-Accept` value by concatenating the key with a standard magic GUID,
    ///   hashing the result using SHA-1, and encoding it in Base64.
    /// - Constructing an HTTP response with the required headers (`Upgrade`, `Connection`, and `Sec-WebSocket-Accept`).
    /// 
    /// The handshake follows the WebSocket protocol as defined in RFC 6455, Section 4.2.2.
    /// 
    /// Limitations:
    /// - Assumes the `request` parameter contains a complete HTTP request.
    /// - Does not validate other aspects of the request, such as HTTP method or version.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the `Sec-WebSocket-Key` header is not found in the request, indicating an invalid WebSocket upgrade request.
    /// </exception>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="EncoderFallbackException"/>
    private static async Task SendHandshakeResponse(IContext context, string request)
    {
        await context.SendAsync(WebsocketHandshakePrefix);
        await context.SendAsync(CreateAcceptKey(request));
        await context.SendAsync(WebsocketHandshakeSuffix);
    }

    /// <summary>
    /// Generates the `Sec-WebSocket-Accept` value required for a WebSocket handshake response.
    /// </summary>
    /// <param name="request">The raw HTTP request string containing headers, including the `Sec-WebSocket-Key`.</param>
    /// <returns>The Base64-encoded SHA-1 hash of the WebSocket key concatenated with a magic string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the `Sec-WebSocket-Key` header is not found in the request, indicating an invalid WebSocket upgrade request.
    /// </exception>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="EncoderFallbackException"/>
    private static string CreateAcceptKey(string request)
    {
        // The WebSocket protocol requires a predefined GUID to be appended to the client's Sec-WebSocket-Key.
        const string magicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        // Convert the request string into a ReadOnlySpan<char> for efficient searching.
        var requestSpan = request.AsSpan();

        // Locate the `Sec-WebSocket-Key` header in the request using case-insensitive search.
        var keyLineStart = requestSpan.IndexOf("Sec-WebSocket-Key:".AsSpan(), StringComparison.OrdinalIgnoreCase);

        // If the key is not found, the request is invalid, and an exception is thrown.
        if (keyLineStart == -1)
        {
            throw new InvalidOperationException("Sec-WebSocket-Key not found in the request.");
        }

        // Extract the portion of the request after `Sec-WebSocket-Key:` (the actual key value follows).
        var keyLine = requestSpan[(keyLineStart + "Sec-WebSocket-Key:".Length)..];

        // Find the end of the key value, which is marked by a newline (`\r\n`).
        var keyEnd = keyLine.IndexOf("\r\n".AsSpan());
        if (keyEnd != -1)
        {
            keyLine = keyLine[..keyEnd]; // Trim the key value to exclude the newline.
        }

        // Trim any surrounding whitespace from the key.
        var key = keyLine.Trim();

        // Concatenate the extracted WebSocket key with the magic string and compute its SHA-1 hash.
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key.ToString() + magicString));

        // Convert the hash to a Base64 string, as required by the WebSocket protocol.
        return Convert.ToBase64String(hash);
    }
}
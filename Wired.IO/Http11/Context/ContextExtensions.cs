using System.Buffers;
using System.Text;
using Wired.IO.Http11.Websockets;

namespace Wired.IO.Http11.Context;

/// <summary>
/// Provides extension methods for handling responses and sending data within the application context.
/// </summary>
public static class ContextExtensions
{
    /// <summary>
    /// Sends a string response to the client.
    /// </summary>
    /// <param name="context">The <see cref="Http11Context"/> representing the current client connection.</param>
    /// <param name="response">The response as a string.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="EncoderFallbackException"/>
    public static async Task SendAsync(this Http11Context context, string response, CancellationToken cancellationToken = default)
    {
        var responseBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(response));
        await context.SendAsync(responseBytes, cancellationToken);
    }

    /// <summary>
    /// Sends a binary response to the client using <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    /// <param name="context">The <see cref="Http11Context"/> representing the current client connection.</param>
    /// <param name="responseBytes">The response as a binary payload.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static async Task SendAsync(this Http11Context context, ReadOnlyMemory<byte> responseBytes, CancellationToken cancellationToken = default)
    {
        await context.Writer.WriteAsync(responseBytes, cancellationToken);
    }

    /// <summary>
    /// Reads data from the <see cref="System.IO.Pipelines.PipeReader"/> associated with the current <see cref="Http11Context"/> into the specified buffer.
    /// </summary>
    /// <param name="context">The <see cref="Http11Context"/> representing the current connection context.</param>
    /// <param name="buffer">
    /// A <see cref="Memory{T}"/> buffer to copy the read data into. The size of this buffer determines the maximum number of bytes that can be read at once.
    /// </param>
    /// <param name="cancellationToken">
    /// A <see cref="CancellationToken"/> that can be used to cancel the read operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous read operation. The result is the number of bytes read into the buffer.
    /// Returns <c>0</c> if the reader has completed and no data was available.
    /// </returns>
    /// <remarks>
    /// - This method does not guarantee that the buffer will be completely filled; it copies as much as is available up to the buffer’s size.
    /// - It advances the <see cref="System.IO.Pipelines.PipeReader"/> only past the copied data, preserving remaining unread data in the buffer.
    /// - Callers are expected to check for a return value of <c>0</c> to detect end-of-stream conditions.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the provided <paramref name="cancellationToken"/>.</exception>
    /// <example>
    /// <code>
    /// var buffer = new byte[1024];
    /// var bytesRead = await context.ReadAsync(buffer, cancellationToken);
    /// if (bytesRead == 0) { /* End of stream */ }
    /// </code>
    /// </example>
    public static async Task<int> ReadAsync(this Http11Context context, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var result = await context.Reader.ReadAsync(cancellationToken);
        var readableBuffer = result.Buffer;

        var bytesToCopy = (int)Math.Min(buffer.Length, readableBuffer.Length);
        if (bytesToCopy == 0 && result.IsCompleted)
        {
            return 0;
        }

        readableBuffer.Slice(0, bytesToCopy).CopyTo(buffer.Span);
        context.Reader.AdvanceTo(readableBuffer.GetPosition(bytesToCopy));

        return bytesToCopy;
    }

    /// <summary>
    /// Reads a WebSocket message from the context's connection and decodes it as a string.
    /// </summary>
    /// <param name="context">The <see cref="Http11Context"/> representing the current connection.</param>
    /// <param name="buffer">The <see cref="Memory{T}"/> buffer to store the incoming data.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to signal operation cancellation.</param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation, containing the decoded WebSocket message.
    /// </returns>
    /// <exception cref="ArgumentException"/>
    /// <exception cref="ArgumentOutOfRangeException"/>
    /// <exception cref="EncoderFallbackException"/>
    /// <exception cref="OperationCanceledException"/>
    public static async Task<(ReadOnlyMemory<byte>, WsFrameType)> WsReadAsync(this Http11Context context, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var receivedBytes = await context.ReadAsync(buffer, cancellationToken);

        if (receivedBytes == 0)
        {
            return (ReadOnlyMemory<byte>.Empty, WsFrameType.Close);
        }

        var decodedFrame = WebsocketUtilities.DecodeFrame(buffer, receivedBytes, out var frameType);
        return (decodedFrame, frameType);
    }

    /// <summary>
    /// Sends a WebSocket message as a UTF-8 encoded string to the context's connection.
    /// </summary>
    /// <param name="context">The <see cref="IContext"/> representing the current connection.</param>
    /// <param name="payload">The message to send as a string.</param>
    /// <param name="opcode"></param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to signal operation cancellation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    /// <exception cref="ArrayTypeMismatchException"/>
    /// <exception cref="ArgumentOutOfRangeException"/>
    public static async Task WsSendAsync(this Http11Context context, string payload, byte opcode = 0x01, CancellationToken cancellationToken = default)
    {
        // Send the response using the context
        await context.WsSendAsync(Encoding.UTF8.GetBytes(payload).AsMemory(), opcode, cancellationToken);
    }

    /// <summary>
    /// Sends a WebSocket message with the specified payload to the client's connection, constructing the frame according to the WebSocket protocol.
    /// 
    /// This method wraps the payload in a WebSocket frame, including the necessary header and length information,
    /// and sends it asynchronously to the client through the provided <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// The <see cref="Http11Context"/> representing the connection to the WebSocket client.
    /// This provides the communication channel through which the frame will be sent.
    /// </param>
    /// <param name="payload">
    /// The payload to send as a <see cref="ReadOnlyMemory{T}"/>. This can be raw binary data or UTF-8 encoded text,
    /// depending on the <paramref name="opcode"/> specified.
    /// </param>
    /// <param name="opcode">
    /// The opcode indicating the type of WebSocket frame to send. Valid values include:
    /// <list type="bullet">
    ///   <item><description><c>0x01</c>: Text frame (UTF-8 encoded text).</description></item>
    ///   <item><description><c>0x02</c>: Binary frame (raw binary data).</description></item>
    ///   <item><description>Control frames (<c>0x08</c>, <c>0x09</c>, <c>0x0A</c>): Close, Ping, and Pong respectively.</description></item>
    /// </list>
    /// The default value is <c>0x01</c>, which indicates a text frame.
    /// </param>
    /// <param name="cancellationToken">
    /// A <see cref="CancellationToken"/> that can be used to cancel the asynchronous operation.
    /// This allows graceful interruption of the send operation if the token is triggered.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation. This task completes when the frame has been sent to the client.
    /// </returns>
    /// <remarks>
    /// <b>WebSocket Frame Construction:</b><br />
    /// - The frame is constructed using <see cref="WebsocketUtilities.BuildWsFrame"/>.
    /// - This includes setting the FIN bit, opcode, and appropriate payload length encoding as per the WebSocket protocol.
    /// 
    /// <b>Performance Notes:</b><br />
    /// - The method uses a pooled memory owner for efficient buffer reuse and minimizes unnecessary allocations.
    /// - The frame memory is automatically disposed when the method completes, ensuring proper resource management.
    /// 
    /// <b>Usage Notes:</b><br />
    /// - Use the appropriate opcode to match the payload type (e.g., <c>0x01</c> for text, <c>0x02</c> for binary).
    /// - Ensure that the <paramref name="payload"/> is formatted correctly for the chosen opcode (e.g., UTF-8 for text frames).
    /// </remarks>
    /// <example>
    /// <code>
    /// var payload = Encoding.UTF8.GetBytes("Hello, WebSocket!").AsMemory();
    /// await context.WsSendAsync(payload, opcode: 0x01, cancellationToken: CancellationToken.None);
    /// 
    /// var binaryData = new byte[] { 0x01, 0x02, 0x03 }.AsMemory();
    /// await context.WsSendAsync(binaryData, opcode: 0x02, cancellationToken: CancellationToken.None);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentException">
    /// Thrown if an invalid opcode is provided to the <paramref name="opcode"/> parameter.
    /// </exception>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    /// <exception cref="ArrayTypeMismatchException"/>
    /// <exception cref="ArgumentOutOfRangeException"/>
    public static async Task WsSendAsync(this Http11Context context, ReadOnlyMemory<byte> payload, byte opcode = 0x01, CancellationToken cancellationToken = default)
    {
        using var frameOwner = WebsocketUtilities.BuildWsFrame(payload, opcode: opcode);
        var frameMemory = frameOwner.Memory;

        // Send the frame to the WebSocket client
        await context.SendAsync(frameMemory, cancellationToken);
    }
}

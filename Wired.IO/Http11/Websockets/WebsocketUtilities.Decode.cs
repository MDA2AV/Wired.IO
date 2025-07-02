using System.Buffers.Binary;
using System.Text;

namespace Wired.IO.Http11.Websockets;

public static partial class WebsocketUtilities
{
    /// <summary>
    /// Decodes a WebSocket frame received from a client, extracting the UTF-8 encoded message payload.
    /// This method handles masking (if present) and supports frames with standard and extended payload lengths
    /// according to the WebSocket protocol (RFC 6455).
    /// </summary>
    /// <param name="buffer">
    /// A <see cref="Memory{T}"/> object containing the raw WebSocket frame received from the client.
    /// The buffer includes the complete WebSocket frame, which consists of a header, optional masking key,
    /// and payload data.
    /// </param>
    /// <param name="length">
    /// The number of bytes in the <paramref name="buffer"/> that constitute the WebSocket frame.
    /// This value ensures that only the relevant portion of the buffer is processed.
    /// </param>
    /// <returns>
    /// A <see cref="string"/> containing the decoded UTF-8 message payload extracted from the WebSocket frame.
    /// If the frame is masked (as required for client-to-server communication), the payload is unmasked before decoding.
    /// </returns>
    /// <remarks>
    /// The WebSocket frame structure is processed as follows:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       The method validates the frame length to ensure it meets the minimum size of 2 bytes.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The payload length is extracted from the second byte. For extended payload lengths:
    ///       <list type="bullet">
    ///         <item><description>16-bit length: The payload length is stored in the next 2 bytes (126).</description></item>
    ///         <item><description>64-bit length: The payload length is stored in the next 8 bytes (127).</description></item>
    ///       </list>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       If the MASK bit is set, the 4-byte masking key is extracted and used to unmask the payload.
    ///       The unmasking process applies an XOR operation between the payload and the masking key.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The payload is then decoded as a UTF-8 string.
    ///     </description>
    ///   </item>
    /// </list>
    /// 
    /// <para>
    /// This method assumes the provided buffer contains a complete WebSocket frame and does not handle fragmentation
    /// or control frames (e.g., Ping, Pong, or Close). It focuses on decoding standard text frames sent from clients.
    /// </para>
    /// 
    /// <para>
    /// <b>Performance Considerations:</b><br />
    /// - Avoids unnecessary allocations by working directly with <see cref="Span{T}"/> where possible.<br />
    /// - Ensures payload extraction and unmasking are performed efficiently, even for large payloads.<br />
    /// - Validates all critical frame components to ensure protocol compliance and robustness against malformed frames.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown in the following cases:
    /// <list type="bullet">
    ///   <item><description>If the frame length is less than the required minimum (2 bytes).</description></item>
    ///   <item><description>If the payload length exceeds the available buffer capacity, indicating an incomplete frame.</description></item>
    ///   <item><description>If the payload length field is invalid or improperly encoded.</description></item>
    /// </list>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"/>
    public static string DecodeMessage(Memory<byte> buffer, int length)
    {
        // Get a span of the input buffer for efficient memory operations
        var span = buffer.Span;

        var opcode = span[0] & 0x0F;
        if (opcode == 0x08) // Close frame
        {
            // Check for optional close payload
            var opPayloadLength = span[1] & 0x7F;
            if (opPayloadLength >= 2)
            {
                // Extract the close code
                var closeCode = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2));
                var reason = opPayloadLength > 2 ? Encoding.UTF8.GetString(span.Slice(4, opPayloadLength - 2)) : null;
                return $"Close frame received. Code: {closeCode}, Reason: {reason ?? "None"}";
            }
            else
            {
                return "Close frame received with no payload.";
            }
        }

        // Validate that the buffer contains at least the minimum frame size (2 bytes: FIN/Opcode + Payload length)
        if (length < 2)
            throw new ArgumentException("Incomplete frame.", nameof(buffer));

        // Determine if the frame is masked (MASK bit set in the second byte)
        var isMasked = (span[1] & 0x80) != 0;

        // Extract the base payload length (7 bits of the second byte)
        var payloadLength = span[1] & 0x7F;

        // Initialize the start position for the payload based on the frame structure
        var payloadStart = 2;

        // Handle extended payload lengths
        switch (payloadLength)
        {
            case 126 when length < 4:
                // If the frame is marked as having a 16-bit payload length but does not contain enough bytes
                throw new ArgumentException("Incomplete frame.", nameof(buffer));
            case 126:
                // Extract the 16-bit payload length (big-endian format) and adjust the payload start index
                payloadLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2));
                payloadStart = 4;
                break;
            case 127 when length < 10:
                // If the frame is marked as having a 64-bit payload length but does not contain enough bytes
                throw new ArgumentException("Incomplete frame.", nameof(buffer));
            case 127:
                // Extract the 64-bit payload length (big-endian format) and adjust the payload start index
                payloadLength = (int)BinaryPrimitives.ReadUInt64BigEndian(span.Slice(2, 8));
                payloadStart = 10;
                break;
        }

        // Validate that the buffer contains enough bytes for the payload (including masking key if applicable)
        if (length < payloadStart + payloadLength + (isMasked ? 4 : 0))
            throw new ArgumentException("Incomplete frame.", nameof(buffer));

        // Extract the masking key if the frame is masked (4 bytes following the header)
        var maskKey = isMasked ? span.Slice(payloadStart, 4) : Span<byte>.Empty;
        payloadStart += isMasked ? 4 : 0;

        // Extract the payload data as a span for further processing
        var payloadSpan = span.Slice(payloadStart, payloadLength);

        // If the frame is masked, unmask the payload using the XOR operation and the masking key
        if (isMasked)
        {
            for (var i = 0; i < payloadSpan.Length; i++)
                payloadSpan[i] ^= maskKey[i % 4];
        }

        // Decode the payload from UTF-8 and return the resulting string
        return Encoding.UTF8.GetString(payloadSpan);
    }

    /// <summary>
    /// Decodes a WebSocket frame from the given buffer, extracting the payload and determining the frame type.
    /// 
    /// This method parses the WebSocket frame header and payload according to the WebSocket protocol (RFC 6455).
    /// It handles frame types such as text, binary, and close frames, as well as optional masking logic for client-to-server frames.
    /// </summary>
    /// <param name="buffer">
    /// A <see cref="Memory{T}"/> containing the raw WebSocket frame received from the client.
    /// This includes the complete frame: header, optional masking key, and payload data.
    /// </param>
    /// <param name="length">
    /// The number of bytes in the <paramref name="buffer"/> that represent the WebSocket frame.
    /// This ensures only the relevant portion of the buffer is processed.
    /// </param>
    /// <param name="frameType">
    /// An output parameter that indicates the type of the WebSocket frame. Possible values:
    /// <list type="bullet">
    ///   <item><description><see cref="Utf8"/>: A UTF-8 encoded text frame.</description></item>
    ///   <item><description><see cref="WsFrameType.Binary"/>: A binary frame.</description></item>
    ///   <item><description><see cref="WsFrameType.Close"/>: A close frame.</description></item>
    /// </list>
    /// </param>
    /// <returns>
    /// A <see cref="ReadOnlyMemory{T}"/> containing the decoded payload of the WebSocket frame.
    /// If the frame is a close frame, the returned memory contains the UTF-8 encoded close message.
    /// For text or binary frames, the returned memory contains the raw payload.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the frame is incomplete or malformed.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the frame opcode is not recognized as a valid WebSocket frame type.
    /// </exception>
    /// <exception cref="EncoderFallbackException"/>
    public static ReadOnlyMemory<byte> DecodeFrame(Memory<byte> buffer, int length, out WsFrameType frameType)
    {
        // Get a span of the input buffer for efficient memory operations.
        var span = buffer.Span;

        // Validate that the buffer contains at least the minimum frame size (2 bytes).
        if (length < 2)
            throw new ArgumentException("Incomplete frame.", nameof(buffer));

        // Extract the opcode from the first byte.
        // The opcode determines the type of frame (e.g., text, binary, close).
        var opcode = span[0] & 0x0F;

        // Handle close frames separately.
        if (opcode == 0x08) // Close frame
        {
            frameType = WsFrameType.Close;

            // Extract the payload length from the second byte.
            var opPayloadLength = span[1] & 0x7F;

            // If the payload length is less than 2, return an empty payload.
            if (opPayloadLength < 2)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            // Extract the close code (2 bytes, big-endian).
            var closeCode = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2));

            // Extract the optional UTF-8 encoded reason phrase (if present).
            var reason = opPayloadLength > 2
                ? Encoding.UTF8.GetString(span.Slice(4, opPayloadLength - 2))
                : null;

            // Construct the close message and return it as a UTF-8 encoded memory block.
            var message = $"Close frame received. Code: {closeCode}, Reason: {reason ?? "None"}";
            return Encoding.UTF8.GetBytes(message).AsMemory();
        }

        // Determine the frame type based on the opcode.
        frameType = opcode switch
        {
            0x00 => WsFrameType.Continue,
            0x01 => WsFrameType.Utf8,
            0x02 => WsFrameType.Binary,
            0x09 => WsFrameType.Ping,
            0x0A => WsFrameType.Pong,
#pragma warning disable S3928
            _ => throw new ArgumentOutOfRangeException(nameof(opcode)) // Invalid opcode.
#pragma warning restore S3928
        };

        // Determine if the frame is masked (MASK bit is set in the second byte).
        var isMasked = (span[1] & 0x80) != 0;

        // Extract the base payload length (7 bits of the second byte).
        var payloadLength = span[1] & 0x7F;

        // Initialize the starting position of the payload based on the frame structure.
        var payloadStart = 2;

        // Handle extended payload lengths.
        switch (payloadLength)
        {
            case 126 when length < 4:
                // If the length is 126 but there are not enough bytes for the extended length field, throw an error.
                throw new ArgumentException("Incomplete frame.", nameof(buffer));
            case 126:
                // Extract the 16-bit extended payload length (big-endian) and adjust the payload start index.
                payloadLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2));
                payloadStart = 4;
                break;
            case 127 when length < 10:
                // If the length is 127 but there are not enough bytes for the extended length field, throw an error.
                throw new ArgumentException("Incomplete frame.", nameof(buffer));
            case 127:
                // Extract the 64-bit extended payload length (big-endian) and adjust the payload start index.
                payloadLength = (int)BinaryPrimitives.ReadUInt64BigEndian(span.Slice(2, 8));
                payloadStart = 10;
                break;
        }

        // Validate that the buffer contains enough bytes for the payload (including masking key if applicable).
        if (length < payloadStart + payloadLength + (isMasked ? 4 : 0))
            throw new ArgumentException("Incomplete frame.", nameof(buffer));

        // Extract the masking key if the frame is masked (4 bytes following the header).
        var maskKey = isMasked ? span.Slice(payloadStart, 4) : Span<byte>.Empty;
        payloadStart += isMasked ? 4 : 0;

        // Extract the payload data as a span for further processing.
        var payloadSpan = span.Slice(payloadStart, payloadLength);

        // If the frame is masked, unmask the payload using the XOR operation and the masking key.
        if (isMasked)
        {
            for (var i = 0; i < payloadSpan.Length; i++)
                payloadSpan[i] ^= maskKey[i % 4];
        }

        // Return the payload as raw bytes in a new memory block.
        return payloadSpan.ToArray();
    }
}
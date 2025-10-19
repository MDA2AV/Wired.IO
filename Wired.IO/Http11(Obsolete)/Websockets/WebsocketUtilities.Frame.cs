using System.Buffers.Binary;
using System.Buffers;
using Wired.IO.MemoryBuffers;

namespace Wired.IO.Http11.Websockets;

public static partial class WebsocketUtilities
{
    /// <summary>
    /// Constructs a WebSocket frame from a given payload following the WebSocket protocol specification.
    /// 
    /// This method dynamically determines the appropriate frame format based on the payload length and opcode.
    /// It creates a WebSocket frame and returns it as an immutable <see cref="ReadOnlyMemory{T}"/>. 
    /// This approach allocates a new array for the final frame, ensuring immutability and safety.
    /// 
    /// <para>
    /// The frame structure includes:
    /// <list type="number">
    ///   <item>
    ///     <description><b>FIN and Opcode (1 byte):</b>
    ///       <list type="bullet">
    ///         <item><description>FIN bit (0x80): Indicates this is the final fragment.</description></item>
    ///         <item><description>Opcode: Indicates the type of frame (<c>0x01</c> for text, <c>0x02</c> for binary).</description></item>
    ///       </list>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description><b>Payload Length:</b>
    ///       <list type="bullet">
    ///         <item><description>0-125 bytes: Encoded in a single byte.</description></item>
    ///         <item><description>126-65535 bytes: Encoded in 2 bytes (big-endian).</description></item>
    ///         <item><description>More than 65535 bytes: Encoded in 8 bytes (big-endian).</description></item>
    ///       </list>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description><b>Payload:</b> The raw data appended after the header.</description>
    ///   </item>
    /// </list>
    /// </para>
    /// 
    /// <b>Performance Notes:</b><br />
    /// - Uses <see cref="ArrayPool{T}"/> for temporary buffer allocation but creates a new immutable array
    ///   for the final output, ensuring thread safety and reducing side effects.
    /// - Suitable for scenarios where immutability and safety are preferred over performance optimization.
    /// </summary>
    /// <param name="payload">
    /// The payload to include in the WebSocket frame, passed as <see cref="ReadOnlyMemory{T}"/>.
    /// </param>
    /// <param name="opcode">
    /// The opcode indicating the type of WebSocket frame:
    /// <list type="bullet">
    ///   <item><description><c>0x01</c>: Text frame (UTF-8 encoded text).</description></item>
    ///   <item><description><c>0x02</c>: Binary frame (raw binary data).</description></item>
    ///   <item><description>Control frames (<c>0x08</c>, <c>0x09</c>, <c>0x0A</c>): Close, Ping, and Pong respectively.</description></item>
    /// </list>
    /// </param>
    /// <returns>
    /// A WebSocket frame as an immutable <see cref="ReadOnlyMemory{T}"/>, ready to send.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the <paramref name="opcode"/> is invalid.
    /// </exception>
    /// <exception cref="ArrayTypeMismatchException"/>
    /// <exception cref="ArgumentOutOfRangeException"/>
    public static ReadOnlyMemory<byte> BuildWsFrameAlloc(ReadOnlyMemory<byte> payload, byte opcode = 0x01)
    {
        // The maximum payload size that can be encoded in a single byte (7 bits) is 125 bytes.
        const int maxSmallPayloadLength = 125;
        // Get the actual length of the payload provided.
        var payloadLength = payload.Length;

        // Validate opcode
        if (opcode != 0x01 && opcode != 0x02 && opcode < 0x08)
        {
            throw new ArgumentException("Invalid opcode. Must be 0x01 (text), 0x02 (binary), or a control frame opcode.", nameof(opcode));
        }

        // Calculate the total response frame size based on the payload length.
        // WebSocket frames have the following format:
        // - 2 bytes for small payloads (1-byte length field)
        // - 4 bytes for extended payloads (2-byte length field for lengths 126-65535)
        // - 10 bytes for large payloads (8-byte length field for lengths > 65535)
        var responseLength = payloadLength switch
        {
            <= maxSmallPayloadLength => 2 + payloadLength,  // 1-byte length field
            <= ushort.MaxValue => 4 + payloadLength,        // 2-byte extended length field
            _ => 10 + payloadLength                         // 8-byte extended length field
        };

        // Rent a buffer from the ArrayPool
        var arrayPool = ArrayPool<byte>.Shared;
        var responseBuffer = arrayPool.Rent(responseLength);

        try
        {
            // Get a span for efficient manipulation of the memory buffer.
            var span = responseBuffer.AsSpan(0, responseLength);

            // Set the first byte of the frame:
            // - FIN bit set (0x80) to indicate this is the final fragment of a message.
            // - Opcode set to 0x01 for a text frame.
            // Set FIN bit (final fragment) and opcode
            span[0] = (byte)(0x80 | opcode);

            // Determine the payload length field encoding based on the size of the payload.
            switch (payloadLength)
            {
                case <= maxSmallPayloadLength:
                    // If the payload length is 125 bytes or less, encode it directly in the second byte.
                    span[1] = (byte)payloadLength;
                    break;
                case <= ushort.MaxValue:
                    // For payload length between 126 and 65535
                    span[1] = 126;
                    BinaryPrimitives.WriteUInt16BigEndian(span[2..4], (ushort)payloadLength);
                    break;
                default:
                    // For payload length exceeding 65535
                    span[1] = 127;
                    BinaryPrimitives.WriteUInt64BigEndian(span[2..10], (ulong)payloadLength);
                    break;
            }

            // Copy the payload data into the frame buffer.
            payload.Span.CopyTo(span[(responseLength - payloadLength)..]);

            // Return the constructed frame as ReadOnlyMemory<byte>.
            // Note: Allocate a new array to return a clean and immutable memory block.
            return new ReadOnlyMemory<byte>(responseBuffer, 0, responseLength).ToArray();
        }
        finally
        {
            // Return the buffer to the ArrayPool
            arrayPool.Return(responseBuffer);
        }
    }

    /// <summary>
    /// Constructs a WebSocket frame from a given payload following the WebSocket protocol specification.
    /// 
    /// This method dynamically determines the appropriate frame format based on the payload length and opcode.
    /// It creates a WebSocket frame and returns it wrapped in a custom <see cref="IMemoryOwner{T}"/> instance.
    /// This approach minimizes allocations by reusing a rented buffer from the <see cref="ArrayPool{T}"/>.
    /// 
    /// <para>
    /// The frame structure includes:
    /// <list type="number">
    ///   <item>
    ///     <description><b>FIN and Opcode (1 byte):</b>
    ///       <list type="bullet">
    ///         <item><description>FIN bit (0x80): Indicates this is the final fragment.</description></item>
    ///         <item><description>Opcode: Indicates the type of frame (<c>0x01</c> for text, <c>0x02</c> for binary).</description></item>
    ///       </list>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description><b>Payload Length:</b>
    ///       <list type="bullet">
    ///         <item><description>0-125 bytes: Encoded in a single byte.</description></item>
    ///         <item><description>126-65535 bytes: Encoded in 2 bytes (big-endian).</description></item>
    ///         <item><description>More than 65535 bytes: Encoded in 8 bytes (big-endian).</description></item>
    ///       </list>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description><b>Payload:</b> The raw data appended after the header.</description>
    ///   </item>
    /// </list>
    /// </para>
    /// 
    /// <b>Performance Notes:</b><br />
    /// - Uses <see cref="ArrayPool{T}"/> to minimize memory allocations, making it suitable for high-performance scenarios.
    /// - Returns an <see cref="IMemoryOwner{T}"/> to ensure proper buffer lifecycle management.
    /// - Ideal for scenarios where performance and reduced garbage collection pressure are critical.
    /// </summary>
    /// <param name="payload">
    /// The payload to include in the WebSocket frame, passed as <see cref="ReadOnlyMemory{T}"/>.
    /// </param>
    /// <param name="opcode">
    /// The opcode indicating the type of WebSocket frame:
    /// <list type="bullet">
    ///   <item><description><c>0x01</c>: Text frame (UTF-8 encoded text).</description></item>
    ///   <item><description><c>0x02</c>: Binary frame (raw binary data).</description></item>
    ///   <item><description>Control frames (<c>0x08</c>, <c>0x09</c>, <c>0x0A</c>): Close, Ping, and Pong respectively.</description></item>
    /// </list>
    /// </param>
    /// <returns>
    /// A custom <see cref="IMemoryOwner{T}"/> containing the WebSocket frame. The consumer is responsible
    /// for disposing of the owner to return the buffer to the pool.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the <paramref name="opcode"/> is invalid.
    /// </exception>
    /// <exception cref="ArrayTypeMismatchException"/>
    /// <exception cref="ArgumentOutOfRangeException"/>
    public static IMemoryOwner<byte> BuildWsFrame(ReadOnlyMemory<byte> payload, byte opcode = 0x01)
    {
        // The maximum payload size that can be encoded in a single byte (7 bits) is 125 bytes.
        const int maxSmallPayloadLength = 125;
        // Get the actual length of the payload provided.
        var payloadLength = payload.Length;

        // Validate opcode
        if (opcode != 0x01 && opcode != 0x02 && opcode < 0x08)
        {
            throw new ArgumentException("Invalid opcode. Must be 0x01 (text), 0x02 (binary), or a control frame opcode.", nameof(opcode));
        }

        // Calculate the total response frame size based on the payload length.
        // // WebSocket frames have the following format:
        // // - 2 bytes for small payloads (1-byte length field)
        // // - 4 bytes for extended payloads (2-byte length field for lengths 126-65535)
        // // - 10 bytes for large payloads (8-byte length field for lengths > 65535)
        var responseLength = payloadLength switch
        {
            <= maxSmallPayloadLength => 2 + payloadLength,  // 1-byte length field
            <= ushort.MaxValue => 4 + payloadLength,        // 2-byte extended length field
            _ => 10 + payloadLength                         // 8-byte extended length field
        };

        // Rent the buffer from ArrayPool
        var arrayPool = ArrayPool<byte>.Shared;
        var responseBuffer = arrayPool.Rent(responseLength);

        try
        {
            // Get a span for efficient manipulation of the memory buffer.
            var span = responseBuffer.AsSpan(0, responseLength);

            // Set the first byte of the frame:
            // - FIN bit set (0x80) to indicate this is the final fragment of a message.
            // - Opcode set to 0x01 for a text frame.
            // Set FIN bit (final fragment) and opcode
            span[0] = (byte)(0x80 | opcode);

            // Write payload length
            switch (payloadLength)
            {
                case <= maxSmallPayloadLength:
                    // If the payload length is 125 bytes or less, encode it directly in the second byte.
                    span[1] = (byte)payloadLength;
                    break;
                case <= ushort.MaxValue:
                    // For payload length between 126 and 65535
                    span[1] = 126;
                    BinaryPrimitives.WriteUInt16BigEndian(span[2..4], (ushort)payloadLength);
                    break;
                default:
                    // For payload length exceeding 65535
                    span[1] = 127;
                    BinaryPrimitives.WriteUInt64BigEndian(span[2..10], (ulong)payloadLength);
                    break;
            }

            // Copy payload into the buffer
            payload.Span.CopyTo(span.Slice(responseLength - payloadLength));

            // Return the buffer wrapped in a memory owner
            return new PooledMemoryOwner(responseBuffer, responseLength, arrayPool);
        }
        catch
        {
            // Return the buffer to the pool in case of an exception
            arrayPool.Return(responseBuffer);
            throw;
        }
    }
}
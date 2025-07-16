using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace Wired.IO.Protocol.Writers;

/// <summary>
/// Implements chunked transfer encoding using a PipeWriter.
/// </summary>
/// <remarks>
/// Response bodies are wrapped into this when no content length is known.
/// </remarks>
public sealed class ChunkedPipeWriter(PipeWriter writer)
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();
    private static readonly ReadOnlyMemory<byte> FinalChunk = "0\r\n\r\n"u8.ToArray();

    /// <summary>
    /// Returns the underlying <see cref="PipeWriter"/> used for writing chunked output.
    /// </summary>
    /// <returns>The inner <see cref="PipeWriter"/>.</returns>
    public PipeWriter GetPipeWriter() => writer;

    /// <summary>
    /// Writes a single chunk synchronously to the pipe using the provided buffer.
    /// </summary>
    /// <param name="buffer">The content to write as a chunk.</param>
    /// <remarks>
    /// This method is optimized using stack allocation and avoids heap allocations
    /// for small content sizes.
    /// </remarks>
    public void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return;
        WriteChunkOptimized(buffer);
    }

    public async ValueTask FlushAsync()
    {
        await writer.FlushAsync();
    }

#if NET9_0_OR_GREATER
    /// <summary>
    /// Writes a single chunk asynchronously using the provided buffer.
    /// </summary>
    /// <param name="buffer">The content to write as a chunk.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous write operation.</returns>
    /// <remarks>
    /// This method uses pooled arrays to reduce allocations for large or dynamic content.
    /// </remarks>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return;

        byte[]? rented = null;
        try
        {
            var chunkSize = buffer.Length;
            var hex = chunkSize.ToString("X");
            var totalSize = hex.Length + 2 + chunkSize + 2;

            rented = ArrayPool<byte>.Shared.Rent(totalSize);
            var span = rented.AsSpan(0, totalSize);
            int pos = 0;

            Encoding.ASCII.GetBytes(hex, span[pos..]);
            pos += hex.Length;

            span[pos++] = (byte)'\r';
            span[pos++] = (byte)'\n';

            buffer.Span.CopyTo(span[pos..]);
            pos += chunkSize;

            span[pos++] = (byte)'\r';
            span[pos++] = (byte)'\n';

            await writer.WriteAsync(rented.AsMemory(0, pos), cancellationToken);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
#endif

    /// <summary>
    /// Writes the final chunk (0-length) to signal the end of the chunked stream.
    /// </summary>
    /// <remarks>
    /// This must be called once all chunked content has been written,
    /// otherwise the client may hang waiting for more data.
    /// </remarks>
    public void Finish()
    {
        writer.Write(FinalChunk.Span);
    }

    /// <summary>
    /// Writes a chunk using a stack-allocated path for maximum efficiency.
    /// </summary>
    /// <param name="buffer">The data buffer to write.</param>
    private void WriteChunkOptimized(ReadOnlySpan<byte> buffer)
    {
        Span<byte> hexBytes = stackalloc byte[16]; // Sufficient for 64-bit sizes
        var hexLength = ToHexBytes(buffer.Length, hexBytes);

        var totalSize = hexLength + 2 + buffer.Length + 2; // hex + \r\n + data + \r\n
        var output = writer.GetSpan(totalSize);

        var pos = 0;

        // Write chunk size in hex
        hexBytes[..hexLength].CopyTo(output[pos..]);
        pos += hexLength;

        output[pos++] = (byte)'\r';
        output[pos++] = (byte)'\n';

        // Write data
        buffer.CopyTo(output[pos..]);
        pos += buffer.Length;

        output[pos++] = (byte)'\r';
        output[pos++] = (byte)'\n';

        writer.Advance(totalSize);
    }

    /// <summary>
    /// Converts an integer value to its hexadecimal representation as ASCII bytes.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="buffer">A span of bytes to receive the hex characters.</param>
    /// <returns>The number of bytes written to the buffer.</returns>
    private static int ToHexBytes(int value, Span<byte> buffer)
    {
        if (value == 0)
        {
            buffer[0] = (byte)'0';
            return 1;
        }

        int pos = 0;
        while (value > 0)
        {
            int digit = value & 0xF;
            buffer[pos++] = (byte)(digit < 10 ? '0' + digit : 'A' + digit - 10);
            value >>= 4;
        }

        buffer[..pos].Reverse();
        return pos;
    }
}
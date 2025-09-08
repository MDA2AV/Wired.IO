using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using Wired.IO.Http11.Context;
using Wired.IO.Http11.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Http11.Middleware;

/// <summary>
/// Middleware for reading and parsing the HTTP request body, supporting both Content-Length and chunked transfer encoding.
/// </summary>
public static class RequestBodyMiddleware
{
    /// <summary>
    /// Handles reading the request body and assigning it to the context's <see cref="IRequest.Content"/> property.
    /// </summary>
    /// <param name="ctx">The <see cref="Http11Context"/> representing the current HTTP connection.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method checks if the request includes a <c>Content-Length</c> header or uses <c>Transfer-Encoding: chunked</c>,
    /// and reads the body accordingly.
    /// </remarks>
    public static async Task HandleAsync(Http11Context ctx)
    {
        var request = Unsafe.As<Http11Request>(ctx.Request);
        var contentLengthAvailable = TryGetContentLength(request.Headers, out _);

        if (contentLengthAvailable)
        {
            // If Content-Length is present, read the body based on that length
            var body = await ExtractBody(ctx.Reader, request.Headers, ctx.CancellationToken);

            ctx.Request.Content = body;

        }
        else
        {
            // If Content-Length is not present, check for chunked transfer encoding
            if (request.Headers.TryGetValue("Transfer-Encoding", out var transferEncoding) &&
                transferEncoding.Equals("chunked", StringComparison.OrdinalIgnoreCase))
            {
                var chunks = new List<byte[]>();

                while (await ExtractChunk(ctx.Reader, ctx.CancellationToken) is { } chunk)
                {
                    if (chunk.Length == 0) // Last chunk indicator "0\r\n\r\n"
                        break;
                    chunks.Add(chunk);
                }

                // Combine all chunks into a single byte array
                ctx.Request.Content = chunks.SelectMany(c => c).ToArray();
            }
        }
    }

    /// <summary>
    /// Extracts a single chunk from a chunked HTTP request body.
    /// </summary>
    /// <param name="reader">The <see cref="PipeReader"/> to read from.</param>
    /// <param name="stoppingToken">Token to cancel the operation.</param>
    /// <returns>The chunk as a byte array, or <c>null</c> if the stream is complete or invalid.</returns>
    /// <exception cref="OperationCanceledException"/>
    public static async Task<byte[]?> ExtractChunk(PipeReader reader, CancellationToken stoppingToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(stoppingToken);
            var buffer = result.Buffer;
            var sequenceReader = new SequenceReader<byte>(buffer);

            // Try to read chunk size line (hex number ending with \r\n)
            if (!TryReadChunkSizeLine(ref sequenceReader, out int chunkSize))
            {
                if (result.IsCompleted)
                    return null;

                // Not enough data yet
                reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            // Final chunk (0\r\n)
            if (chunkSize == 0)
            {
                if (PipeReaderUtilities.TryAdvanceTo(sequenceReader, "\r\n"u8, out var trailerEnd))
                {
                    reader.AdvanceTo(trailerEnd); // Skip the final CRLF
                    return []; // End signal
                }

                // Need more data to consume the trailer \r\n
                reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            // Calculate position where full chunk body ends (size + CRLF)
            var chunkStart = sequenceReader.Position;
            var chunkEnd = buffer.GetPosition(chunkSize + 2, chunkStart); // +2 for \r\n

            if (buffer.Length < buffer.Slice(chunkStart, chunkSize + 2).Length)
            {
                // Not enough data for the chunk + \r\n
                if (result.IsCompleted)
                    return null;

                reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            // Extract the chunk content
            var chunk = buffer.Slice(chunkStart, chunkSize).ToArray();

            // Advance past the chunk and its trailing \r\n
            reader.AdvanceTo(chunkEnd);
            return chunk;
        }
    }

    /// <summary>
    /// Attempts to read the chunk size line (in hex) from the stream.
    /// </summary>
    /// <param name="reader">The <see cref="SequenceReader{Byte}"/> positioned at the start of the chunk size line.</param>
    /// <param name="size">The parsed chunk size.</param>
    /// <returns><c>true</c> if the chunk size line was successfully read; otherwise <c>false</c>.</returns>
    private static bool TryReadChunkSizeLine(ref SequenceReader<byte> reader, out int size)
    {
        size = 0;

        var line = new SequenceReader<byte>(reader.Sequence);
        if (!line.TryReadTo(out ReadOnlySpan<byte> lineSpan, (byte)'\n'))
            return false;

        if (lineSpan.EndsWith("\r"u8))
            lineSpan = lineSpan[..^1];

        if (!int.TryParse(Encoding.ASCII.GetString(lineSpan), System.Globalization.NumberStyles.HexNumber, null, out size))
            return false;

        reader.Advance(lineSpan.Length + 2); // +2 for \r\n
        return true;
    }

    /// <summary>
    /// Extracts the HTTP request body from the pipe reader stream based on the Content-Length header.
    /// </summary>
    /// <param name="reader">The pipe reader containing the request body data.</param>
    /// <param name="headers">The HTTP headers string containing the Content-Length header.</param>
    /// <param name="stoppingToken">A cancellation token to stop the operation.</param>
    /// <returns>
    /// A string containing the request body if Content-Length is valid and the body is read successfully;
    /// otherwise, null.
    /// </returns>
    /// <remarks>
    /// This method:
    /// 1. Parses the Content-Length header to determine how many bytes to read
    /// 2. Attempts to read the body in a single operation if possible
    /// 3. Falls back to reading the body in fragments if necessary
    /// 4. Decodes the body bytes using UTF-8 encoding
    /// 
    /// If the connection closes before reading the complete body (based on Content-Length),
    /// an InvalidOperationException is thrown.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the stream ends unexpectedly before reading the complete body.
    /// </exception>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="ArgumentException"/>
    /// <exception cref="ArgumentOutOfRangeException"/>
    /// <exception cref="ArrayTypeMismatchException"/>
    /// <exception cref="OperationCanceledException"/>
    public static async Task<ReadOnlyMemory<byte>> ExtractBody(
        PipeReader reader,
        PooledDictionary<string, string> headers,
        CancellationToken stoppingToken)
    {
        // Try to get the Content-Length from headers
        if (!TryGetContentLength(headers, out var contentLength))
            return ReadOnlyMemory<byte>.Empty; // No Content-Length header or invalid value

        // Request hasn't body
        if (contentLength == 0)
            return ReadOnlyMemory<byte>.Empty;

        // Allocate a buffer to store the body
        var bodyBuffer = new byte[contentLength];
        var bytesRead = 0;

        // Read initial data from the PipeReader
        var result = await reader.ReadAsync(stoppingToken);
        var buffer = result.Buffer;

        // Optimize for common case: entire body available in one read
        if (buffer.Length >= contentLength)
        {
            // Copy the body data to our buffer
            buffer.Slice(0, contentLength).CopyTo(bodyBuffer);

            // Advance the reader past the body
            reader.AdvanceTo(buffer.GetPosition(contentLength));

            return bodyBuffer;
        }

        // Handle fragmented body (less common case)
        while (bytesRead < contentLength)
        {
            // Read more data from the PipeReader
            result = await reader.ReadAsync(stoppingToken);
            buffer = result.Buffer;

            // Calculate how much to read from current buffer
            var toRead = Math.Min(buffer.Length, contentLength - bytesRead);

            // Copy the data to our body buffer
            buffer.Slice(0, toRead).CopyTo(bodyBuffer.AsMemory(bytesRead).Span);
            bytesRead += (int)toRead;

            // Advance the PipeReader
            reader.AdvanceTo(buffer.GetPosition(toRead));

            // Check for premature end of stream
            if (result.IsCompleted && bytesRead < contentLength)
            {
                throw new InvalidOperationException("Unexpected end of stream while reading the body.");
            }
        }

        // Decode and return the complete body
        return bodyBuffer;
    }

    /// <summary>
    /// Attempts to parse the Content-Length header value as an integer.
    /// </summary>
    /// <param name="headers">The HTTP headers collection.</param>
    /// <param name="contentLength">The parsed Content-Length value, if successful.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
    private static bool TryGetContentLength(PooledDictionary<string, string> headers, out int contentLength)
    {
        var headerAvailable = headers.TryGetValue("Content-Length", out var header);

        if (headerAvailable)
            return int.TryParse(header, out contentLength);

        contentLength = 0;
        return false;
    }
}
using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Wired.IO.Http11Express.Context;
using Wired.IO.Http11Express.Request;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Writers;
using Wired.IO.Utilities;
using Wired.IO.Utilities.StringCache;

namespace Wired.IO.Http11Express;

#pragma warning disable CS1591 // (We document via XML comments below; suppress warnings if any members stay undocumented.)

// Public non-generic facade
public sealed class WiredHttp11Express : WiredHttp11Express<Http11ExpressContext> { }

/// <summary>
/// Minimal, high-performance HTTP/1.1 request handler that parses request lines and headers
/// directly from a pooled <see cref="PipeReader"/> buffer and dispatches to a user-supplied pipeline.
/// </summary>
/// <typeparam name="TContext">
/// Concrete context type used per-connection/request. Must inherit <see cref="Http11ExpressContext"/> and have a public parameter-less constructor.
/// </typeparam>
/// <remarks>
/// <para>
/// Designed for low-allocation scenarios. Context instances are pooled with <see cref="ObjectPool{T}"/>; the same connection
/// can carry multiple HTTP/1.1 requests (pipe-lining). Parsing is specialized for the common case of a single-segment buffer,
/// and falls back to a multi-segment parser otherwise.
/// </para>
/// <para>
/// This type does not implement full HTTP semantics (e.g., request body handling, chunked transfer decoding, or HTTP/2).
/// It focuses on parsing the request line and headers efficiently for typical benchmarking/plaintext-style endpoints.
/// </para>
/// </remarks>
public partial class WiredHttp11Express<TContext> : IHttpHandler<TContext>
    // TContext can be a super type of Http11ExpressContext
    where TContext : Http11ExpressContext, new()
{
    /// <summary>
    /// Parser state machine for the multi-segment path.
    /// </summary>
    private enum State
    {
        /// <summary>Expecting the request line: METHOD SP PATH SP HTTP/VERSION CRLF</summary>
        StartLine,
        /// <summary>Reading header lines until a blank line (CRLF) is found.</summary>
        Headers,
        /// <summary>Headers complete; body (if any) would be processed next.</summary>
        Body
    }

    /// <summary>
    /// Object pool used to recycle request contexts for reduced allocations and improved performance.
    /// </summary>
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new PipelinedContextPoolPolicy(), 4096);

    /// <summary>
    /// Pool policy that defines how to create and reset pooled <typeparamref name="{TContext}"/> instances.
    /// </summary>
    private class PipelinedContextPoolPolicy : PooledObjectPolicy<TContext>
    {
        /// <summary>
        /// Creates a new instance of <typeparamref name="{TContext}"/>.
        /// </summary>
        public override TContext Create() => new();

        /// <summary>
        /// Resets the context before returning it to the pool.
        /// </summary>
        /// <param name="context">The context instance to return.</param>
        /// <returns><c>true</c> if the context can be reused; otherwise, <c>false</c>.</returns>
        public override bool Return(TContext context)
        {
            context.Clear(); // User-defined reset method to clean internal state.
            return true;
        }
    }

    /// <summary>
    /// Handles a client connection using a <see cref="Stream"/>, wiring it to <see cref="PipeReader"/>/<see cref="PipeWriter"/>
    /// and processing zero or more HTTP/1.1 requests in sequence.
    /// </summary>
    /// <param name="stream">The underlying duplex stream (e.g., network stream).</param>
    /// <param name="pipeline">The application pipeline delegate that processes a fully-parsed request.</param>
    /// <param name="stoppingToken">Cancellation token signaling server shutdown.</param>
    /// <returns>A task that completes when the connection closes or parsing finishes.</returns>
    /// <remarks>
    /// The connection is half-managed here: exceptions are swallowed to avoid noisy logs in benchmark scenarios; the stream is closed when the reader/writer complete.
    /// </remarks>
    public async Task HandleClientAsync(Stream stream, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        // Rent a context object from the pool
        var context = ContextPool.Get();

        // Assign a new CancellationTokenSource to support per-request cancellation
        var cts = new CancellationTokenSource();
        context.CancellationToken = cts.Token;

        // Wrap the stream in a PipeReader and PipeWriter for efficient buffered reads/writes
        context.Reader = PipeReader.Create(stream,
            new StreamPipeReaderOptions(
                MemoryPool<byte>.Shared, 
                leaveOpen: false,
                bufferSize: 4096*4, 
                minimumReadSize: 1024));

        context.Writer = PipeWriter.Create(stream,
            new StreamPipeWriterOptions(
                MemoryPool<byte>.Shared, 
                leaveOpen: false,
                minimumBufferSize: 512));

        try
        {
            await ProcessRequestsAsync(context, pipeline);
        }
        catch
        {
            // Swallow all exceptions; connection will be closed silently
            await cts.CancelAsync();
        }
        finally
        {
            // Gracefully complete the reader/writer to release underlying resources
            await context.Reader.CompleteAsync();
            await context.Writer.CompleteAsync();

            // Return context to pool for reuse
            ContextPool.Return(context);
        }
    }

    /// <summary>
    /// Reads from the <see cref="PipeReader"/> and parses as many complete HTTP requests as are currently available,
    /// dispatching each to <paramref name="pipeline"/>.
    /// </summary>
    /// <param name="context">The pooled per-connection context.</param>
    /// <param name="pipeline">Application delegate to invoke once a request line and headers are parsed.</param>
    /// <remarks>
    /// Uses a fast path for single-segment buffers to minimize copying and branching. For multi-segment buffers,
    /// a <see cref="SequenceReader{T}"/> is used to parse across segments safely.
    /// </remarks>
    [SkipLocalsInit]
    private static async Task ProcessRequestsAsync(TContext context, Func<TContext, Task> pipeline)
    {
        var state = State.StartLine;

        while (true)
        {
            var readResult = await context.Reader.ReadAsync();
            var buffer = readResult.Buffer;

            var isCompleted = readResult.IsCompleted;
                
            if (buffer.IsEmpty && isCompleted)
                return;

            var flush = false;

            // Hot path: A new request is starting, and the buffer is a single segment
            // If some of the request is already read, always fall back to multi-segment path
            // This avoids complex state management in the single-segment path
            // This optimizes for the common case of small requests that fit in one segment (vast majority of cases)
            if (buffer.IsSingleSegment && state == State.StartLine)
            {
                var currentPosition = 0;

                while (true)
                {
                    if (buffer.Length == 0 || isCompleted)
                        break;

                    var requestReceived = ExtractHeaderFromSingleSegment(context, ref buffer, ref currentPosition);

                    if (!requestReceived)
                    {
                        context.Reader.AdvanceTo(buffer.GetPosition(0), buffer.GetPosition(buffer.FirstSpan.Length));
                        break;
                    }

                    context.Reader.AdvanceTo(buffer.GetPosition(currentPosition));
                    
                    state = State.Body;
                    
                    var bodyReceived = TryExtractBodyFromSingleSegment(context, ref buffer, ref currentPosition, out var bodyEmpty);
                    if (!bodyReceived)
                        break;

                    // Diogo here, fix the reader position for chunked case

                    if (!bodyEmpty)
                        context.Reader.AdvanceTo(buffer.GetPosition(currentPosition));

                    // Handle the request pipeline
                    await pipeline(context);

                    // Respond
                    if(context.Response is not null && context.Response.IsActive())
                        WriteResponse(context);

                    // Clear context for next request
                    context.Clear();
                    
                    // Signal that there is something to flush
                    flush = true;

                    state = State.StartLine;

                    if (currentPosition == buffer.Length) // There is no more data, need to ReadAsync()
                        break;
                }
            }
            // Slower path: multi-segment buffer or already partway through a request
            // This handles cases where the request line or headers span multiple segments
            else
            {
                var currentPosition = buffer.Start;

                while (true)
                {
                    if (buffer.Length == 0 || isCompleted)
                        break;

                    if (state != State.Body)
                    {
                        var requestReceived =
                            ExtractHeaderFromMultipleSegment(context, ref buffer, ref currentPosition, ref state);

                        context.Reader.AdvanceTo(currentPosition, buffer.End);

                        if (!requestReceived)
                            break;

                        state = State.Body;
                    }

                    //Try to get the body, if the full body isn't read, break
                    var bodyReceived = TryExtractBodyFromMultipleSegment(context, ref buffer, ref currentPosition, ref state);

                    if (!bodyReceived)
                        break;

                    await pipeline(context);
                    context.Clear();
                    flush = true;

                    state = State.StartLine;

                    if (buffer.Slice(currentPosition).IsEmpty) // There is no more data, need to ReadAsync()
                        break;
                }
            }

            if (flush)
                await context.Writer.FlushAsync();
        }
    }

    /// <summary>
    /// Attempts to extract the HTTP request body from a single-segment <see cref="ReadOnlySequence{T}"/> buffer.
    /// </summary>
    /// <param name="context">The HTTP request context to populate with body data.</param>
    /// <param name="buffer">The input buffer containing the body.</param>
    /// <param name="position">
    /// The current offset in the single segment span.  
    /// Updated to point just after the body if extraction succeeds.
    /// </param>
    /// <param name="bodyEmpty">
    /// Output flag indicating whether the request body was empty or absent.  
    /// <c>true</c> if no body is present, otherwise <c>false</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the body was fully extracted or no body is required;  
    /// <c>false</c> if more data is needed from the transport.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the Content-Length or chunked encoding format is invalid.
    /// </exception>
    [SkipLocalsInit]
    private static bool TryExtractBodyFromSingleSegment(
        TContext context, 
        ref ReadOnlySequence<byte> buffer, 
        ref int position, 
        out bool bodyEmpty)
    {
        bodyEmpty = true;

        // Content-Length header present
        var contentLengthAvailable = context.Request.Headers.TryGetValue(ContentLength, out var contentLengthValue);
        if (contentLengthAvailable)
        {
            var validContentLength = int.TryParse(contentLengthValue, out var contentLength);
            if (contentLength <= 0 || !validContentLength)
            {
                return true; // Invalid Content-Length header
            }

            bodyEmpty = false;

            var remainingBytes = buffer.FirstSpan.Length - position;

            if(remainingBytes < contentLength)
                return false; // Not enough bytes yet

            context.Request.ContentLength = contentLength;
            context.Request.Content = ArrayPool<byte>.Shared.Rent(contentLength);
            buffer.FirstSpan.Slice(position, contentLength).CopyTo(context.Request.Content);

            position += contentLength;
        }

        // Transfer-Encoding header present
        var transferEncodingAvailable = context.Request.Headers.TryGetValue(TransferEncoding, out var transferEncodingValue);
        if (transferEncodingAvailable && transferEncodingValue.Equals("chunked", StringComparison.OrdinalIgnoreCase))
        {
            var currentOffset = 0;
            var bufferSpan = buffer.FirstSpan[position..];

            var bodyEndIndex = bufferSpan.IndexOf("0\r\n\r\n"u8);
            if (bodyEndIndex == -1)
                return false;   // Not enough bytes yet

            context.Request.Content = ArrayPool<byte>.Shared.Rent(bodyEndIndex);

            while (true)
            {
                var chunkSizeEnd = bufferSpan[currentOffset..].IndexOf(Crlf);
                var validChunkSize = int.TryParse(bufferSpan[currentOffset..(currentOffset + chunkSizeEnd)], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var currentChunkSize);
                if (!validChunkSize)
                    throw new InvalidOperationException("Invalid chunk size");

                if (currentChunkSize == 0) // End of body detected
                {
                    if (chunkSizeEnd + currentOffset != bodyEndIndex + 1)
                        throw new InvalidOperationException("Invalid chunked body termination");

                    position += 5; // Move past "0\r\n\r\n"
                    return true;
                }

                bodyEmpty = false;

                var destinationSpan = context.Request.Content.AsSpan().Slice(currentOffset, currentChunkSize);
                bufferSpan.Slice(chunkSizeEnd + 2, currentChunkSize).CopyTo(destinationSpan);

                context.Request.ContentLength += currentChunkSize;
                currentOffset += chunkSizeEnd + 2 + currentChunkSize + 2; // Move past chunk size line, chunk data, and trailing CRLF
                position += currentOffset;
            }
        }

        return true;
    }

    
    /// <summary>
    /// Attempts to extract the HTTP request body from a multi-segment <see cref="ReadOnlySequence{T}"/> buffer.
    /// </summary>
    /// <param name="context">The HTTP request context to populate with body data.</param>
    /// <param name="buffer">The input buffer containing the body.</param>
    /// <param name="position">
    /// The current position in the buffer.  
    /// Updated to point just after the body if extraction succeeds.
    /// </param>
    /// <param name="state">Parser state, may be advanced after body extraction.</param>
    /// <returns>
    /// <c>true</c> if the body was fully extracted or no body is required;  
    /// <c>false</c> if more data is needed from the transport.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the Content-Length or chunked encoding format is invalid.
    /// </exception>
    private static bool TryExtractBodyFromMultipleSegment(
        TContext context,
        ref ReadOnlySequence<byte> buffer,
        ref SequencePosition position,
        ref State state)
    {
        // Content-Length header present
        var contentLengthAvailable = context.Request.Headers.TryGetValue(ContentLength, out var contentLengthValue);
        if (contentLengthAvailable)
        {
            var validContentLength = int.TryParse(contentLengthValue, out var contentLength);

            if (!validContentLength || contentLength < 0)
                return false; // Invalid Content-Length header

            if (buffer.Slice(position).Length < contentLength)
                return false; // Not enough bytes yet

            context.Request.Content = ArrayPool<byte>.Shared.Rent(contentLength);
            buffer.Slice(position, contentLength).CopyTo(context.Request.Content);

            context.Request.ContentLength = contentLength;

            position = buffer.GetPosition(contentLength, position);
        }

        var transferEncodingAvailable = context.Request.Headers.TryGetValue(TransferEncoding, out var transferEncodingValue);
        if (transferEncodingAvailable && transferEncodingValue.Equals("chunked", StringComparison.OrdinalIgnoreCase))
        {
            var reader = new SequenceReader<byte>(buffer.Slice(position));
            var currentOffset = context.Request.Content != null ? context.Request.ContentLength : 0;

            while (true)
            {
                if (!reader.TryReadTo(out ReadOnlySequence<byte> chunkSizeSequence, Crlf))
                    return false; // Not enough bytes yet

                var chunkSizeSpan = chunkSizeSequence.ToSpan();
                if (!TryParseChunkSize(chunkSizeSpan, out var chunkSize))
                    throw new InvalidOperationException("Invalid chunk size");

                if (chunkSize == 0) // End of body detected
                {
                    if (!reader.TryReadTo(out ReadOnlySequence<byte> _, Crlf))
                        return false; // Not enough bytes yet

                    position = reader.Position;
                    break;
                }

                if (reader.Remaining < chunkSize + 2) // +2 for trailing CRLF
                    return false; // Not enough bytes yet

                if (context.Request.Content == null)
                {
                    // Estimate a reasonable buffer size for the first chunk, will be re-rented if more chunks arrive
                    context.Request.Content = ArrayPool<byte>.Shared.Rent(chunkSize);
                }
                else if (context.Request.Content.Length < currentOffset + chunkSize)
                {
                    // Grow the buffer if needed
                    var newBuffer = ArrayPool<byte>.Shared.Rent(currentOffset + chunkSize);
                    context.Request.Content.AsSpan(0, currentOffset).CopyTo(newBuffer);
                    ArrayPool<byte>.Shared.Return(context.Request.Content);
                    context.Request.Content = newBuffer;
                }

                var destinationSpan = context.Request.Content.AsSpan(currentOffset, chunkSize);
                if (!reader.TryCopyTo(destinationSpan))
                    return false; // Not enough bytes yet

                currentOffset += chunkSize;

                // Advance past chunk data and trailing CRLF
                reader.Advance(chunkSize);
                if (!reader.TryReadTo(out ReadOnlySequence<byte> _, Crlf))
                    return false; // Not enough bytes yet
            }

            context.Request.ContentLength = currentOffset;
            position = reader.Position;
        }

        return true;
    }

    // Helper for parsing chunk size (hex)
    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseChunkSize(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        int i = 0;
        for (; i < span.Length; i++)
        {
            byte b = span[i];
            if (b >= '0' && b <= '9')
                value = (value << 4) + (b - '0');
            else if (b >= 'A' && b <= 'F')
                value = (value << 4) + (b - 'A' + 10);
            else if (b >= 'a' && b <= 'f')
                value = (value << 4) + (b - 'a' + 10);
            else
                return false;
        }
        return i > 0;
    }

    /// <summary>
    /// Parses the request line and headers from a single-segment buffer.
    /// </summary>
    /// <param name="context">Target request context to populate.</param>
    /// <param name="buffer">The read-only buffer (single segment).</param>
    /// <param name="position">Current integer offset in the first segment; updated to point just after the header terminator.</param>
    /// <returns><see langword="true"/> if a complete header block was parsed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the request line is malformed.</exception>
    [SkipLocalsInit]
    private static bool ExtractHeaderFromSingleSegment(
        TContext context,
        ref ReadOnlySequence<byte> buffer, 
        ref int position)
    {
        // Hot path, single segment buffer
        var bufferSpan = buffer.FirstSpan[position..];
        var fullHeaderIndex = bufferSpan.IndexOf("\r\n\r\n"u8);

        if (fullHeaderIndex == -1)
            return false;

        // Whole headers are present for the request
        // Parse first header

        var lineEnd = bufferSpan.IndexOf("\r\n"u8);
        var firstHeader = bufferSpan[..lineEnd];

        var firstSpace = firstHeader.IndexOf(Space);
        if (firstSpace == -1)
            throw new InvalidOperationException("Invalid request line");

        context.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(firstHeader[..firstSpace]);
        
        var secondSpaceRelative = firstHeader[(firstSpace + 1)..].IndexOf(Space);
        if (secondSpaceRelative == -1)
            throw new InvalidOperationException("Invalid request line");

        var secondSpace = firstSpace + secondSpaceRelative + 1;
        var url = firstHeader[(firstSpace + 1)..secondSpace];
        var queryStart = url.IndexOf(Question); // (byte)'?'

        if (queryStart != -1)
        {
            // Route has params
            context.Request.Route = CachedData.CachedRoutes.GetOrAdd(url[..queryStart]);
            var querySpan = url[(queryStart + 1)..];
            var current = 0;
            while (current < querySpan.Length)
            {
                var separator = querySpan[current..].IndexOf(QuerySeparator); // (byte)'&'
                ReadOnlySpan<byte> pair;

                if (separator == -1)
                {
                    pair = querySpan[current..];
                    current = querySpan.Length;
                }
                else
                {
                    pair = querySpan.Slice(current, separator);
                    current += separator + 1;
                }

                var equalsIndex = pair.IndexOf(Equal); // (byte)'='
                if (equalsIndex == -1)
                    break;

                context.Request.QueryParameters?
                    .TryAdd(CachedData.CachedQueryKeys.GetOrAdd(pair[..equalsIndex]),
                         Encoders.Utf8Encoder.GetString(pair[(equalsIndex + 1)..]));
            }
        }
        else
        {
            // Url is same as route
            context.Request.Route = CachedData.CachedRoutes.GetOrAdd(url);
        }

        // Parse remaining headers
        var lineStart = 0;
        while (true)
        {
            lineStart += lineEnd + 2;

            lineEnd = bufferSpan[lineStart..].IndexOf("\r\n"u8);
            if (lineEnd == 0)
            {
                // All Headers read
                break;
            }

            var header = bufferSpan.Slice(lineStart, lineEnd);
            var colonIndex = header.IndexOf(Colon);

            if (colonIndex == -1)
            {
                // Malformed header
                continue;
            }

            var headerKey = header[..colonIndex];
            var headerValue = header[(colonIndex + 2)..];

            context.Request.Headers
                .TryAdd(CachedData.PreCachedHeaderKeys.GetOrAdd(headerKey), CachedData.PreCachedHeaderValues.GetOrAdd(headerValue));
        }

        position += fullHeaderIndex + 4;
        //context.Reader.AdvanceTo(buffer.GetPosition(position));

        return true;
    }

    [SkipLocalsInit]
    //[MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool ExtractHeaderFromSingleSegment2(
        TContext context,
        ref ReadOnlySequence<byte> buffer,
        ref int position)
    {
        ReadOnlySpan<byte> first = buffer.FirstSpan;
        if ((uint)position >= (uint)first.Length) return false;

        // ✅ Only parse the unconsumed tail
        ReadOnlySpan<byte> bufferSpan = first[position..];

        // Fast completeness check: CRLFCRLF
        int headerEndRel = bufferSpan.IndexOf("\r\n\r\n"u8);
        if (headerEndRel < 0) return false;

        int advance = headerEndRel + 4;

        // ----- Request line -----
        int reqLineEnd = bufferSpan.IndexOf("\r\n"u8);
        if ((uint)reqLineEnd >= (uint)headerEndRel)
            throw new InvalidOperationException("Invalid request line");

        ReadOnlySpan<byte> reqLine = bufferSpan[..reqLineEnd];

        //const byte SP = (byte)' ';
        //const byte Q = (byte)'?';
        //const byte AMP = (byte)'&';
        //const byte EQ = (byte)'=';
        //const byte COL = (byte)':';

        int firstSpace = reqLine.IndexOf(Space);
        if (firstSpace < 0) throw new InvalidOperationException("Invalid request line");

        context.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(reqLine[..firstSpace]);

        int secondSpaceRel = reqLine[(firstSpace + 1)..].IndexOf(Space);
        if (secondSpaceRel < 0) throw new InvalidOperationException("Invalid request line");
        int secondSpace = firstSpace + 1 + secondSpaceRel;

        ReadOnlySpan<byte> url = reqLine[(firstSpace + 1)..secondSpace];

        // ----- Route + query -----
        int qIdx = url.IndexOf(Question);
        if (qIdx >= 0)
        {
            context.Request.Route = CachedData.CachedRoutes.GetOrAdd(url[..qIdx]);

            ReadOnlySpan<byte> query = url[(qIdx + 1)..];
            int cur = 0;
            while (cur < query.Length)
            {
                int sep = query[cur..].IndexOf(QuerySeparator);
                ReadOnlySpan<byte> pair = (sep < 0) ? query[cur..] : query.Slice(cur, sep);
                cur = (sep < 0) ? query.Length : cur + sep + 1;

                if (pair.Length == 0) continue;
                int eq = pair.IndexOf(Equal);
                if (eq <= 0 || eq == pair.Length - 1) continue;

                var key = CachedData.CachedQueryKeys.GetOrAdd(pair[..eq]);
                // TODO: URL-decode value bytes if needed before GetString
                var val = Encoders.Utf8Encoder.GetString(pair[(eq + 1)..]);
                context.Request.QueryParameters?.TryAdd(key, val);
            }
        }
        else
        {
            context.Request.Route = CachedData.CachedRoutes.GetOrAdd(url);
        }

        // ----- Headers -----
        int lineStart = reqLineEnd + 2; // after request line CRLF
        while (true)
        {
            if (lineStart > headerEndRel) break;

            int rel = bufferSpan[lineStart..].IndexOf("\r\n"u8);
            if (rel < 0) break; // should not happen due to headerEndRel

            int lineLen = rel;
            if (lineLen == 0) break; // blank line => end of headers

            ReadOnlySpan<byte> header = bufferSpan.Slice(lineStart, lineLen);
            int colon = header.IndexOf(Colon);
            if (colon > 0)
            {
                ReadOnlySpan<byte> keyBytes = header[..colon];

                int valStart = colon + 1;
                if (valStart < header.Length && header[valStart] == Space) valStart++;

                ReadOnlySpan<byte> valueBytes = (valStart <= header.Length)
                    ? header[valStart..]
                    : ReadOnlySpan<byte>.Empty;

                context.Request.Headers.TryAdd(
                    CachedData.PreCachedHeaderKeys.GetOrAdd(keyBytes),
                    CachedData.PreCachedHeaderValues.GetOrAdd(valueBytes));
            }

            lineStart += lineLen + 2;
        }

        // Advance absolute position in the original first span
        position += advance;
        return true;
    }

    /// <summary>
    /// Parses the request line and headers from a multi-segment buffer using <see cref="SequenceReader{T}"/>.
    /// </summary>
    /// <param name="context">Target request context to populate.</param>
    /// <param name="buffer">The read-only buffer (may span multiple segments).</param>
    /// <param name="position">The current sequence position; updated as bytes are consumed.</param>
    /// <param name="state">State machine indicating where to resume parsing.</param>
    /// <returns><see langword="true"/> if a complete header block was parsed; otherwise <see langword="false"/>.</returns>
    [SkipLocalsInit]
    private static bool ExtractHeaderFromMultipleSegment(
        TContext context,
        ref ReadOnlySequence<byte> buffer,
        ref SequencePosition position,
        ref State state)
    {
        // Parse the complete headers, taking off from where it left off
        var headerReader = new SequenceReader<byte>(buffer.Slice(position));

        if (state == State.StartLine)
        {
            if (!TryParseRequestLine(ref headerReader, context.Request))
                return false;
        }
        
        // Header route was read, update state
        position = headerReader.Position;
        state = State.Headers;

        // Parse remaining headers
        while (true)
        {
            if (!headerReader.TryReadTo(out ReadOnlySequence<byte> headerLine, Crlf))
                return false;
            
            position = headerReader.Position;
            
            if (headerLine.Length == 0)
                break; // Empty line indicates end of headers

            ParseHeaderLine(in headerLine, context.Request);

            if (headerReader.End)
                return false;
            
        }
        // All headers were read
        //state = State.Body;
        position = headerReader.Position;
        return true;
    }

    // ---- Parsing helpers ----
    /// <summary>
    /// Parses the request line (method, URL, version) from the reader position and advances past CRLF.
    /// </summary>
    /// <param name="reader">Sequence reader positioned at the start of the request line.</param>
    /// <param name="request">Target request object to populate.</param>
    /// <returns><see langword="true"/> if the full request line was parsed; otherwise <see langword="false"/>.</returns>
    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private static bool TryParseRequestLine(ref SequenceReader<byte> reader, IExpressRequest request)
    {
        // Read method
        if (!reader.TryReadTo(out ReadOnlySequence<byte> methodSequence, (byte)' '))
            return false;

        request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(methodSequence.ToSpan());

        // Read URL/path
        if (!reader.TryReadTo(out ReadOnlySequence<byte> urlSequence, (byte)' '))
            return false;
        
        ParseUrl(in urlSequence, request);

        // Skip HTTP version (read to end of line)
        return reader.TryReadTo(out ReadOnlySequence<byte> _, Crlf);
    }
    /// <summary>
    /// Parses the URL and optional query string into the request route and query dictionary.
    /// </summary>
    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private static void ParseUrl(in ReadOnlySequence<byte> urlSequence, IExpressRequest request)
    {
        var urlSpan = urlSequence.ToSpan();
        var queryStart = urlSpan.IndexOf((byte)'?');

        if (queryStart != -1)
        {
            // URL has query parameters
            var routeSpan = urlSpan[..queryStart];
            request.Route = CachedData.CachedRoutes.GetOrAdd(routeSpan);

            // Parse query parameters
            ParseQueryParameters(urlSpan[(queryStart + 1)..], request);
        }
        else
        {
            // Simple URL without query parameters  
            request.Route = CachedData.CachedRoutes.GetOrAdd(urlSpan);
        }
    }
    /// <summary>
    /// Parses a query string in <c>key=value&amp;key2=value2</c> form into <see cref="IExpressRequest.QueryParameters"/>.
    /// </summary>
    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private static void ParseQueryParameters(in ReadOnlySpan<byte> querySpan, IExpressRequest request)
    {
        var current = 0;
        while (current < querySpan.Length)
        {
            var separator = querySpan[current..].IndexOf((byte)'&');
            ReadOnlySpan<byte> pair;

            if (separator == -1)
            {
                pair = querySpan[current..];
                current = querySpan.Length;
            }
            else
            {
                pair = querySpan.Slice(current, separator);
                current += separator + 1;
            }

            var equalsIndex = pair.IndexOf((byte)'=');
            if (equalsIndex == -1)
                continue;

            var key = CachedData.CachedQueryKeys.GetOrAdd(pair[..equalsIndex]);
            var value = Encoding.UTF8.GetString(pair[(equalsIndex + 1)..]);

            request.QueryParameters?.TryAdd(key, value);
        }
    }
    /// <summary>
    /// Parses a single header line and adds it to <see cref="IExpressRequest.Headers"/>.
    /// </summary>
    /// <param name="headerLine">A buffer containing a single header line with trailing CRLF removed.</param>
    /// <param name="request">Target request.</param>
    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private static void ParseHeaderLine(in ReadOnlySequence<byte> headerLine, IExpressRequest request)
    {
        var headerSpan = headerLine.ToSpan();
        var colonIndex = headerSpan.IndexOf((byte)':');

        if (colonIndex == -1)
            return; // Malformed header, skip

        var headerKey = headerSpan[..colonIndex];

        // Skip colon and optional whitespace
        var valueStart = colonIndex + 1;
        while (valueStart < headerSpan.Length && headerSpan[valueStart] == (byte)' ')
            valueStart++;

        var headerValue = headerSpan[valueStart..];

        request.Headers?.TryAdd(
            CachedData.PreCachedHeaderKeys.GetOrAdd(headerKey),
            CachedData.PreCachedHeaderValues.GetOrAdd(headerValue));
    }

    // ---- Constants & literals ----

    /// <summary>CRLF delimiter used for line termination.</summary>
    private static ReadOnlySpan<byte> Crlf => "\r\n"u8;
    private static ReadOnlySpan<byte> CrlfCrlf => "\r\n\r\n"u8;

    private const string ContentLength = "Content-Length";
    private const string TransferEncoding = "Transfer-Encoding";
            
    private const byte Space = 0x20; // ' '
    private const byte Question = 0x3F; // '?'
    private const byte QuerySeparator = 0x26; // '&'
    private const byte Equal = 0x3D; // '='
    private const byte Colon = 0x3A; // ':'
    private const byte SemiColon = 0x3B; // ';'
}
/// <summary>
/// Caches commonly-seen strings (routes, header keys/values, methods) to avoid repeated allocations
/// and string interning during hot paths.
/// </summary>
/// <remarks>
/// Backed by custom fast hash caches sized for typical HTTP workloads. The pre-cached sets include common
/// request methods and frequently-present headers/values seen in benchmarks (e.g., TechEmpower Plaintext JSON).
/// </remarks>
internal static class CachedData
{
    /// <summary>Cache of parsed routes (path components of the URL).</summary>
    internal static readonly FastHashStringCache32 CachedRoutes = new FastHashStringCache32();
    /// <summary>Cache of query-string keys.</summary>
    internal static readonly FastHashStringCache32 CachedQueryKeys = new FastHashStringCache32();
    /// <summary>Pre-cached HTTP methods (8 common verbs).</summary>
    internal static readonly FastHashStringCache16 PreCachedHttpMethods = new FastHashStringCache16([
        "GET",
        "POST",
        "PUT",
        "DELETE",
        "PATCH",
        "HEAD",
        "OPTIONS",
        "TRACE"
    ], 8);
    /// <summary>Pre-cached header keys commonly present in requests.</summary>
    internal static readonly FastHashStringCache32 PreCachedHeaderKeys = new FastHashStringCache32([
        "Host",
        "User-Agent",
        "Cookie",
        "Accept",
        "Accept-Language",
        "Connection"
    ]);
    /// <summary>Pre-cached header values commonly seen on the wire.</summary>
    internal static readonly FastHashStringCache32 PreCachedHeaderValues = new FastHashStringCache32([
        "keep-alive",
        "server",
    ]);
}
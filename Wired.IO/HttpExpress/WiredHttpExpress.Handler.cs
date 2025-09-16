using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Writers;
using Wired.IO.Utilities;
using Wired.IO.Utilities.StringCache;

namespace Wired.IO.HttpExpress;

public class WiredHttpExpress<TContext> : IHttpHandler<TContext>
    where TContext : HttpExpressContext, new()
{
    /// <summary>
    /// Object pool used to recycle request contexts for reduced allocations and improved performance.
    /// </summary>
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new PipelinedContextPoolPolicy(), 4096);

    /// <summary>
    /// Pool policy that defines how to create and reset pooled <typeparamref name="TContext"/> instances.
    /// </summary>
    private class PipelinedContextPoolPolicy : PooledObjectPolicy<TContext>
    {
        /// <summary>
        /// Creates a new instance of <typeparamref name="TContext"/>.
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
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: false));

        try
        {
            // Loop to handle multiple requests on the same connection (keep-alive)
            await ProcessRequestsAsync(context, pipeline);
            //await ProcessRequestsAsyncReader(context, pipeline);
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
    
    private enum State
    {
        StartLine,
        Headers,
        Body
    }

    private async Task ProcessRequestsAsync(TContext context, Func<TContext, Task> pipeline)
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

            if (buffer.IsSingleSegment)
            {
                var currentPosition = 0;

                while (true)
                {
                    if (buffer.Length == 0 || isCompleted)
                        break;

                    var requestReceived = ExtractHeaderFromSingleSegment(context, ref buffer, ref currentPosition);

                    context.Reader.AdvanceTo(buffer.GetPosition(currentPosition));

                    if (!requestReceived)
                        break;

                    await pipeline(context);
                    context.Clear();
                    flush = true;

                    if (currentPosition == buffer.Length) // There is no more data, need to ReadAsync()
                        break;
                }
            }
            else
            {
                var currentPosition = buffer.Start;

                while (true)
                {
                    if (buffer.Length == 0 || isCompleted)
                        break;

                    var requestReceived = ExtractHeaderFromMultipleSegment(context, ref buffer, ref currentPosition, ref state);

                    context.Reader.AdvanceTo(currentPosition, buffer.End);

                    if (!requestReceived)
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
        state = State.Body;
        position = headerReader.Position;
        return true;
    }

    // ---- Parsing helpers ----
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    private static ReadOnlySpan<byte> Crlf => "\r\n"u8;
    private const byte Space = 0x20;
    private const byte Question = 0x3F;
    private const byte QuerySeparator = 0x26;
    private const byte Equal = 0x3D;
    private const byte Colon = 0x3A;
    private const byte SemiColon = 0x3B;
}
internal static class CachedData
{
    internal static readonly FastHashStringCache32 CachedRoutes = new FastHashStringCache32();
    internal static readonly FastHashStringCache32 CachedQueryKeys = new FastHashStringCache32();
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
    internal static readonly FastHashStringCache32 PreCachedHeaderKeys = new FastHashStringCache32([
        "Host",
        "User-Agent",
        "Cookie",
        "Accept",
        "Accept-Language",
        "Connection"
    ]);
    internal static readonly FastHashStringCache32 PreCachedHeaderValues = new FastHashStringCache32([
        "keep-alive",
        "server",
    ]);
}
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
    
    // Consider using ValueTask here
    private async Task ProcessRequestsAsync(TContext context, Func<TContext, Task> pipeline)
    {
        while (true)
        {
            var readResult = await context.Reader.ReadAsync();
            var buffer = readResult.Buffer;
            var isCompleted = readResult.IsCompleted;
                
            if (buffer.IsEmpty && isCompleted)
            {
                return;
            }

            var currentPosition = 0;
            var flush = false;

            while (true)
            {
                // Try to extract a request to the context
                // Process it (async call)
                // If there is more data in the pipe reader go again
                
                var requestReceived = ReadRequestHeaders2(context, ref buffer, isCompleted, ref currentPosition);
                
                if (!requestReceived)
                {
                    break;
                }
                
                await pipeline(context);
                context.Clear();
                flush = true;

                if (currentPosition == buffer.Length)
                {
                    // There is no more data, need to ReadAsync()
                    break;
                }
            }
            if(flush)
                await context.Writer.FlushAsync();
        }
    }
    
    private async Task ProcessRequestsAsyncReader(TContext context, Func<TContext, Task> pipeline)
    {
        var state = State.StartLine;
        while (true)
        {
            var readResult = await context.Reader.ReadAsync();
            var buffer = readResult.Buffer;
            var isCompleted = readResult.IsCompleted;
                
            if (buffer.IsEmpty && isCompleted)
            {
                return;
            }

            var currentPosition = buffer.Start;
            var flush = false;

            while (true)
            {
                // Try to extract a request to the context
                // Process it (async call)
                // If there is more data in the pipe reader go again
                
                var requestReceived = ReadRequestHeaders(context, ref buffer, isCompleted, ref currentPosition, ref state);
                
                context.Reader.AdvanceTo(currentPosition, buffer.End);
                
                if (!requestReceived)
                    break;
                
                await pipeline(context);
                context.Clear();
                
                flush = true;
                
                state = State.StartLine;

                if (buffer.Slice(currentPosition).IsEmpty)
                //if (buffer.GetOffset(currentPosition) == buffer.GetOffset(buffer.End))
                {
                    // There is no more data, need to ReadAsync()
                    break;
                }
            }

            if (!flush) 
                continue;
            await context.Writer.FlushAsync();
        }
    }

    private static bool ReadRequestHeaders(
        TContext context,
        ref ReadOnlySequence<byte> buffer,
        bool isCompleted,
        ref SequencePosition position,
        ref State state)
    {
        if (buffer.Length == 0 || isCompleted)
            throw new IOException("Client disconnected");
        
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
            if (!headerReader.TryReadTo(out ReadOnlySequence<byte> headerLine, CRLF))
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

    private bool ReadRequestHeaders(TContext context, ref ReadOnlySequence<byte> buffer, bool isCompleted, ref int position)
    {
       var reader = context.Reader;

       while (true)
       {
           if (buffer.Length == 0 || isCompleted) 
               throw new IOException("Client disconnected");
           
           // Hot path, single segment buffer
           if (buffer.IsSingleSegment)
           {
               var bufferSpan = buffer.FirstSpan[position..];
               var fullHeaderIndex = bufferSpan.IndexOf("\r\n\r\n"u8);

               if (fullHeaderIndex != -1)
               {
                   // Whole headers are present for the request
                   // Parse first header

                   var lineEnd = bufferSpan.IndexOf("\r\n"u8);
                   var firstHeader = bufferSpan[..lineEnd];

                   var firstSpace = firstHeader.IndexOf(Bytes.Space);
                   if(firstSpace == -1)
                       throw new InvalidOperationException("Invalid request line");

                   context.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(firstHeader[..firstSpace]);

                   var secondSpaceRelative = firstHeader[(firstSpace + 1)..].IndexOf(Bytes.Space);
                   if (secondSpaceRelative == -1)
                       throw new InvalidOperationException("Invalid request line");

                   var secondSpace = firstSpace + secondSpaceRelative + 1;
                   var url = firstHeader[(firstSpace + 1)..secondSpace];
                   var queryStart = url.IndexOf(Bytes.Question); // (byte)'?'

                   if (queryStart != -1)
                   {
                       // Route has params
                       context.Request.Route = CachedData.CachedRoutes.GetOrAdd(url[..queryStart]);
                       var querySpan = url[(queryStart + 1)..];
                       var current = 0;
                       while (current < querySpan.Length)
                       {
                           var separator = querySpan[current..].IndexOf(Bytes.QuerySeparator); // (byte)'&'
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

                           var equalsIndex = pair.IndexOf(Bytes.Equal); // (byte)'='
                           if (equalsIndex == -1)
                           {
                               break;
                           }
                           
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
                       var colonIndex = header.IndexOf(Bytes.Colon);

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
                   reader.AdvanceTo(buffer.GetPosition(position));

                   return true;
               }
               else
               {
                   // It is a single segment but the request is not complete
                   return false;
               }
           }
           else
           {
               // It is not a single segment, fallback to use SequenceReader
           }

           position = default;
           return false;
       }
    }

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
        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, CRLF))
            return false;
        
        return true;
    }
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

    private struct WriterAdapter : IBufferWriter<byte>
    {
        public PipeWriter Writer;

        public WriterAdapter(PipeWriter writer)
            => Writer = writer;

        public void Advance(int count)
            => Writer.Advance(count);

        public Memory<byte> GetMemory(int sizeHint = 0)
            => Writer.GetMemory(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0)
            => Writer.GetSpan(sizeHint);
    }
    
    private static ReadOnlySpan<byte> CRLF => "\r\n"u8;
    //private static readonly byte[] CRLF = "\r\n"u8.ToArray();
    //private static readonly byte[] CRLFCRLF = "\r\n\r\n"u8.ToArray();
    private const byte CR = (byte)'\r';
    private const byte LF = (byte)'\n';
    private const byte SP = (byte)' ';
    private const byte COLON = (byte)':';
    private const byte AMP = (byte)'&';
    private const byte EQ = (byte)'=';
    private const byte QMARK = (byte)'?';

    private bool ReadRequestHeaders2(
        TContext context, 
        ref ReadOnlySequence<byte> buffer, 
        bool isCompleted,
        ref int position)
    {
        var reader = context.Reader;

        while (true)
        {
            if (buffer.Length == 0 || isCompleted)
                throw new IOException("Client disconnected");

            if (buffer.IsSingleSegment)
            {
                // Hot path: single segment
                var span = buffer.FirstSpan; // whole segment
                if ((uint)position >= (uint)span.Length)
                    return false;

                // Find end of headers with a single linear scan: "\r\n\r\n"
                int headerEnd = FindDoubleCRLF(span, position);
                if (headerEnd < 0)
                    return false; // need more data

                // Parse request line: <METHOD> SP <URL> SP HTTP/...
                int lineStart = position;
                int lineEnd = IndexOfCRLF(span, lineStart);
                if (lineEnd <= lineStart)
                    throw new InvalidOperationException("Invalid request line");

                // First space
                int firstSpace = IndexOfByte(span, SP, lineStart, lineEnd);
                if (firstSpace < 0)
                    throw new InvalidOperationException("Invalid request line");

                var methodSpan = span.Slice(lineStart, firstSpace - lineStart);
                context.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(methodSpan);

                // Second space
                int urlStart = firstSpace + 1;
                int secondSpace = IndexOfByte(span, SP, urlStart, lineEnd);
                if (secondSpace < 0)
                    throw new InvalidOperationException("Invalid request line");

                var urlSpan = span.Slice(urlStart, secondSpace - urlStart);

                // Route + query parse (single pass, no extra slicing)
                int qIdx = urlSpan.IndexOf(QMARK);
                if (qIdx >= 0)
                {
                    // Route before '?'
                    var route = urlSpan[..qIdx];
                    context.Request.Route = CachedData.CachedRoutes.GetOrAdd(route);

                    // Query after '?'
                    var query = urlSpan[(qIdx + 1)..];
                    int qi = 0;
                    while (qi < query.Length)
                    {
                        int amp = query[qi..].IndexOf(AMP);
                        ReadOnlySpan<byte> pair;
                        if (amp < 0)
                        {
                            pair = query[qi..];
                            qi = query.Length;
                        }
                        else
                        {
                            pair = query.Slice(qi, amp);
                            qi += amp + 1;
                        }

                        int eq = pair.IndexOf(EQ);
                        if (eq <= 0) // empty key or no '='
                            continue;

                        var key = pair[..eq];
                        var value = pair[(eq + 1)..]; // URL-decoding omitted by design (fast path)
                        context.Request.QueryParameters?.TryAdd(
                            CachedData.CachedQueryKeys.GetOrAdd(key),
                            Encoders.Utf8Encoder.GetString(value));
                    }
                }
                else
                {
                    context.Request.Route = CachedData.CachedRoutes.GetOrAdd(urlSpan);
                }

                // Parse headers: from (lineEnd+2) up to headerEnd (exclusive of CRLFCRLF)
                int p = lineEnd + 2;
                while (p < headerEnd)
                {
                    // next CRLF
                    int thisEnd = IndexOfCRLF(span, p);
                    if (thisEnd < 0 || thisEnd > headerEnd)
                        throw new InvalidOperationException("Malformed headers");

                    if (thisEnd == p)
                    {
                        // blank line — should not hit before headerEnd, but guard anyway
                        break;
                    }

                    // header: Key ":" [SP] Value
                    int colon = IndexOfByte(span, COLON, p, thisEnd);
                    if (colon > p)
                    {
                        var key = span.Slice(p, colon - p);

                        int valStart = colon + 1;
                        if (valStart < thisEnd && span[valStart] == SP)
                            valStart++;

                        if (valStart <= thisEnd)
                        {
                            var value = span.Slice(valStart, thisEnd - valStart);
                            context.Request.Headers.TryAdd(
                                CachedData.PreCachedHeaderKeys.GetOrAdd(key),
                                CachedData.PreCachedHeaderValues.GetOrAdd(value));
                        }
                    }
                    // else malformed header, ignore

                    p = thisEnd + 2;
                }

                // consume including the "\r\n\r\n"
                int newPos = headerEnd + 4;
                position = newPos;

                // Advance the PipeReader: consumed=newPos, examined=newPos (we've fully consumed through headers)
                reader.AdvanceTo(buffer.GetPosition(newPos));

                return true;
            }
            else
            {
                // Not enough data yet to find CRLFCRLF across segments
                return false;
            }
        }
    }

    // ---- helpers (all inlined by JIT) ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindDoubleCRLF(ReadOnlySpan<byte> s, int start)
    {
        // scan for CRLFCRLF
        for (int i = start; i + 3 < s.Length; i++)
        {
            if (s[i] == CR && s[i + 1] == LF && s[i + 2] == CR && s[i + 3] == LF)
                return i;
        }
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOfCRLF(ReadOnlySpan<byte> s, int start)
    {
        // find "\r\n" starting at 'start'; returns index of '\r'
        for (int i = start; i + 1 < s.Length; i++)
        {
            if (s[i] == CR && s[i + 1] == LF)
                return i;
        }
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOfByte(ReadOnlySpan<byte> s, byte b, int start, int endExclusive)
    {
        var slice = s.Slice(start, endExclusive - start);
        int rel = slice.IndexOf(b);
        return rel < 0 ? -1 : start + rel;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetHeaderBlock(in ReadOnlySequence<byte> seq, out ReadOnlySequence<byte> block)
    {
        // Search for CRLFCRLF across segments using a SequenceReader (no allocs)
        var sr = new SequenceReader<byte>(seq);
        if (sr.TryReadTo(out ReadOnlySequence<byte> headers, CRLF, advancePastDelimiter: true)) // first CRLF (end of request line)
        {
            // Now look for the blank line end (CRLF just after a CRLF)
            // We already consumed first CRLF in reader; so search for CRLFCRLF in remaining:
            // A portable approach: search for "\r\n\r\n" from the original seq
            // Note: small loop over sequence segments keeps it cheap.
            var rest = seq.Slice(0, seq.Length); // work from original
            var it = rest.GetEnumerator();
            SequencePosition pos = rest.Start;
            int crlfCount = 0;
            while (it.MoveNext())
            {
                var span = it.Current.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    byte c = span[i];
                    if (c == CR)
                    {
                        // peek next if possible by checking LF next
                        if (i + 1 < span.Length && span[i + 1] == LF)
                        {
                            crlfCount++;
                            if (crlfCount == 2)
                            {
                                var end = rest.GetPosition(i + 1, pos);           // position of LF
                                block = rest.Slice(0, end); // up to (and including) the second CRLF’s LF
                                return true;
                            }
                            i++; // skip LF
                            continue;
                        }
                        else
                        {
                            crlfCount = 0;
                        }
                    }
                    else
                    {
                        crlfCount = 0;
                    }
                }
                pos = rest.GetPosition(span.Length, pos);
            }
        }

        block = default;
        return false;
    }

    // Re-uses the exact logic of the single-segment path against a flat span copy.
    private static void ParseRequestFromSpan(TContext context, ReadOnlySpan<byte> span, ref int position, int headerEnd)
    {
        int lineStart = position;
        int lineEnd = IndexOfCRLF(span, lineStart);
        if (lineEnd <= lineStart) throw new InvalidOperationException("Invalid request line");

        int firstSpace = IndexOfByte(span, SP, lineStart, lineEnd);
        if (firstSpace < 0) throw new InvalidOperationException("Invalid request line");

        var methodSpan = span.Slice(lineStart, firstSpace - lineStart);
        context.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(methodSpan);

        int urlStart = firstSpace + 1;
        int secondSpace = IndexOfByte(span, SP, urlStart, lineEnd);
        if (secondSpace < 0) throw new InvalidOperationException("Invalid request line");

        var urlSpan = span.Slice(urlStart, secondSpace - urlStart);

        int qIdx = urlSpan.IndexOf(QMARK);
        if (qIdx >= 0)
        {
            var route = urlSpan[..qIdx];
            context.Request.Route = CachedData.CachedRoutes.GetOrAdd(route);

            var query = urlSpan[(qIdx + 1)..];
            int qi = 0;
            while (qi < query.Length)
            {
                int amp = query[qi..].IndexOf(AMP);
                ReadOnlySpan<byte> pair;
                if (amp < 0) { pair = query[qi..]; qi = query.Length; }
                else { pair = query.Slice(qi, amp); qi += amp + 1; }

                int eq = pair.IndexOf(EQ);
                if (eq <= 0) continue;

                var key = pair[..eq];
                var value = pair[(eq + 1)..];
                context.Request.QueryParameters?.TryAdd(
                    CachedData.CachedQueryKeys.GetOrAdd(key),
                    Encoders.Utf8Encoder.GetString(value));
            }
        }
        else
        {
            context.Request.Route = CachedData.CachedRoutes.GetOrAdd(urlSpan);
        }

        int p = lineEnd + 2;
        while (p < headerEnd)
        {
            int thisEnd = IndexOfCRLF(span, p);
            if (thisEnd < 0 || thisEnd > headerEnd) break;

            int colon = IndexOfByte(span, COLON, p, thisEnd);
            if (colon > p)
            {
                var key = span.Slice(p, colon - p);
                int valStart = colon + 1;
                if (valStart < thisEnd && span[valStart] == SP) valStart++;
                var value = span.Slice(valStart, thisEnd - valStart);

                context.Request.Headers.TryAdd(
                    CachedData.PreCachedHeaderKeys.GetOrAdd(key),
                    CachedData.PreCachedHeaderValues.GetOrAdd(value));
            }

            p = thisEnd + 2;
        }

        position = headerEnd + 4;
    }
}

internal static class Bytes
{
    public const byte Space = 0x20;
    public const byte Question = 0x3F;
    public const byte QuerySeparator = 0x26;
    public const byte Equal = 0x3D;
    public const byte Colon = 0x3A;
    public const byte SemiColon = 0x3B;
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
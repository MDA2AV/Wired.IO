using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Wired.IO.Protocol.Handlers;

namespace Wired.IO.HttpExpress;

public class WiredHttpExpress<TContext> : IHttpHandler<TContext>
    where TContext : HttpExpressContext, new()
{
    /// <summary>
    /// Object pool used to recycle request contexts for reduced allocations and improved performance.
    /// </summary>
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new PipelinedContextPoolPolicy(), 512);

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
            new StreamPipeReaderOptions(MemoryPool<byte>.Shared, leaveOpen: false, bufferSize: 1024));

        context.Writer = PipeWriter.Create(stream,
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: false));

        try
        {
            // Loop to handle multiple requests on the same connection (keep-alive)
            while (await ReadRequestHeaders(context))
            {
                // Invoke user-defined middleware pipeline
                await pipeline(context);

                // Clear context state for reuse in the next request (on the same socket)
                context.Clear();

                // If the client indicated "Connection: close", exit the loop
                //if (context.Request.ConnectionType is not ConnectionType.KeepAlive)
                //    break;
            }
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

    public static class Bytes
    {
        public const byte Space = 0x20;
        public const byte Question = 0x3F;
        public const byte QuerySeparator = 0x26;
        public const byte Equal = 0x3D;
        public const byte Colon = 0x3A;
        public const byte SemiColon = 0x3B;
    }

    private static readonly FastHashStringCache32 CachedRoutes = new FastHashStringCache32();
    private static readonly FastHashStringCache32 CachedQueryKeys = new FastHashStringCache32();
    private static readonly FastHashStringCache16 PreCachedHttpMethods = new FastHashStringCache16([
        "GET",
        "POST",
        "PUT",
        "DELETE",
        "PATCH",
        "HEAD",
        "OPTIONS",
        "TRACE"
    ], 8);

    private static readonly FastHashStringCache32 PreCachedHeaderKeys = new FastHashStringCache32(
    [
        "Host",
        "User-Agent",
        "Cookie",
        "Accept",
        "Accept-Language",
        "Connection"
    ]);

    private static readonly FastHashStringCache32 PreCachedHeaderValues = new FastHashStringCache32([
        "keep-alive",
        "server",
    ]);

    public static async ValueTask<bool> ReadRequestHeaders(TContext context, bool minimal = true)
    { 
        var reader = context.Reader;

        while (true)
        {
            // Update the result from pipe reader
            var result = await reader.ReadAsync(context.CancellationToken);
            var buffer = result.Buffer;

            if (buffer.Length == 0 || result.IsCompleted) 
                throw new IOException("Client disconnected");

            // Hot path, single segment buffer
            if (buffer.IsSingleSegment)
            {
                var bufferSpan = buffer.FirstSpan;
                var fullHeaderIndex = bufferSpan.IndexOf("\r\n\r\n"u8);

                if (fullHeaderIndex != -1)
                {
                    // Whole headers are present for the request

                    // Parse first header

                    var lineEnd = bufferSpan.IndexOf("\r\n"u8);
                    var firstHeader = bufferSpan[..lineEnd];
                    //var firstHeader = bufferSpan[..firstHeaderIndex];

                    var firstSpace = firstHeader.IndexOf(Bytes.Space);
                    if(firstSpace == -1)
                        throw new InvalidOperationException("Invalid request line");

                    context.Request.HttpMethod = PreCachedHttpMethods.GetOrAdd(firstHeader[..firstSpace]);

                    var secondSpaceRelative = firstHeader[(firstSpace + 1)..].IndexOf(Bytes.Space);
                    if (secondSpaceRelative == -1)
                        throw new InvalidOperationException("Invalid request line");

                    var secondSpace = firstSpace + secondSpaceRelative + 1;

                    var url = firstHeader[(firstSpace + 1)..secondSpace];

                    var queryStart = url.IndexOf(Bytes.Question); // (byte)'?'

                    if (queryStart != -1)
                    {
                        // Route has params

                        context.Request.Route = CachedRoutes.GetOrAdd(url[..queryStart]);

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
                                .TryAdd(CachedQueryKeys.GetOrAdd(pair[..equalsIndex]), 
                                        Encoding.UTF8.GetString(pair[(equalsIndex + 1)..]));
                        }
                    }
                    else
                    {
                        // Url is same as route

                        context.Request.Route = CachedRoutes.GetOrAdd(url);
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
                            .TryAdd(PreCachedHeaderKeys.GetOrAdd(headerKey), PreCachedHeaderValues.GetOrAdd(headerValue));
                    }
                    

                    reader.AdvanceTo(buffer.GetPosition(fullHeaderIndex + 4));

                    return true;
                }
            }

            return false;
        }
    }


    private const int DelimiterLen = 4;
    private const int Keep = DelimiterLen - 1;

    private static async ValueTask<ReadOnlySequence<byte>> ReadHeadersAsync(PipeReader reader)
    {
        while (true)
        {
            var result = await reader.ReadAsync();
            var buffer = result.Buffer;

            if (result.IsCompleted && buffer.Length == 0)
                throw new IOException("Client disconnected");

            if (buffer.IsSingleSegment)
            {
                var span = buffer.FirstSpan;
                var pos = span.IndexOf("\r\n\r\n"u8);
                if (pos >= 0)
                {
                    var after = buffer.GetPosition(pos + DelimiterLen, buffer.Start);
                    var headersWithDelimiter = buffer.Slice(0, after);
                    reader.AdvanceTo(after, after);
                    return headersWithDelimiter;
                }
            }
            else
            {
                var sr = new SequenceReader<byte>(buffer);
                if (sr.TryReadTo(out ReadOnlySequence<byte> _, "\r\n\r\n"u8, advancePastDelimiter: true))
                {
                    var after = sr.Position;
                    var headersWithDelimiter = buffer.Slice(0, after);
                    reader.AdvanceTo(after, after);
                    return headersWithDelimiter;
                }
            }

            // Not found yet → preserve trailing 3 bytes so split delimiter can complete.
            if (buffer.Length > Keep)
            {
                var consumeTo = buffer.GetPosition(buffer.Length - Keep);
                reader.AdvanceTo(consumeTo, buffer.End);
            }
            else
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
            }

            if (result.IsCompleted)
                throw new InvalidOperationException("Connection closed before headers completed.");
        }
    }
}

public static class SequenceSearch
{
    public static bool TryAdvanceSingleSegment(
        in ReadOnlySequence<byte> buffer,
        ReadOnlySpan<byte> delimiter,
        out SequencePosition after)
    {
        after = default;

        var span = buffer.FirstSpan;
        var idx = span.IndexOf(delimiter);
        if (idx < 0)
            return false;

        after = buffer.GetPosition(idx + delimiter.Length, buffer.Start);
        return true;
    }

    public static bool TryAdvanceMultiSegment(
        in ReadOnlySequence<byte> buffer,
        ReadOnlySpan<byte> delimiter,
        out SequencePosition after)
    {
        after = default;

        if (delimiter.Length == 0)
            throw new ArgumentException("Delimiter cannot be empty.", nameof(delimiter));

        byte first = delimiter[0];
        var searchFrom = buffer.Start;

        while (true)
        {
            var pos = buffer.Slice(searchFrom).PositionOf(first);
            if (pos == null)
                return false;

            var candidate = pos.Value;
            if (StartsWithAt(buffer, candidate, delimiter))
            {
                after = buffer.GetPosition(delimiter.Length, candidate);
                return true;
            }

            searchFrom = buffer.GetPosition(1, candidate);
        }
    }

    // Helper: check if delimiter matches starting at position
    private static bool StartsWithAt(
        in ReadOnlySequence<byte> buffer,
        SequencePosition start,
        ReadOnlySpan<byte> delimiter)
    {
        var seq = buffer.Slice(start);
        var remaining = delimiter;
        var pos = seq.Start;

        while (!remaining.IsEmpty && seq.TryGet(ref pos, out var mem, advance: true))
        {
            var span = mem.Span;
            int take = Math.Min(span.Length, remaining.Length);

            if (!span.Slice(0, take).SequenceEqual(remaining.Slice(0, take)))
                return false;

            remaining = remaining.Slice(take);
        }

        return remaining.IsEmpty;
    }
}

public sealed class FastHashStringCache16
{
    private readonly Dictionary<ushort, string> _map; // Changed from ulong

    public FastHashStringCache16(List<string>? preCacheableStrings, int capacity = 256)
    {
        _map = new Dictionary<ushort, string>(capacity); // Changed from ulong
        if (preCacheableStrings is not null)
        {
            foreach (var preCacheableString in preCacheableStrings)
            {
                AddPredefined(preCacheableString);
            }
        }
    }

    public FastHashStringCache16(int capacity = 256)
    {
        _map = new Dictionary<ushort, string>(capacity); // Changed from ulong
    }

    public string GetOrAdd(ReadOnlySpan<byte> bytes)
    {
        ushort h = Fnv1a16(bytes); // Changed from ulong
        if (_map.TryGetValue(h, out var s))
            return s;                           // may be a false hit if collision
        s = Encoders.Utf8Encoder.GetString(bytes);
        _map[h] = s;
        return s;
    }

    private void AddPredefined(string stringToCache)
    {
        var bytes = Encoders.Utf8Encoder.GetBytes(stringToCache);
        ushort h = Fnv1a16(bytes); // Changed from ulong
        _map[h] = stringToCache;
    }

    private static ushort Fnv1a16(ReadOnlySpan<byte> data)
    {
        const ushort offset = 0x811C; // 33084
        const ushort prime = 0x0101; // 257
        ushort h = offset;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= prime;
        }
        return h;
    }
}

public sealed class FastHashStringCache32
{
    private readonly Dictionary<uint, string> _map; // Changed from ulong

    public FastHashStringCache32(List<string>? preCacheableStrings, int capacity = 256)
    {
        _map = new Dictionary<uint, string>(capacity); // Changed from ulong
        if (preCacheableStrings is not null)
        {
            foreach (var preCacheableString in preCacheableStrings)
            {
                AddPredefined(preCacheableString);
            }
        }
    }

    public FastHashStringCache32(int capacity = 256)
    {
        _map = new Dictionary<uint, string>(capacity); // Changed from ulong
    }

    public string GetOrAdd(ReadOnlySpan<byte> bytes)
    {
        uint h = Fnv1a32(bytes); // Changed from ulong
        if (_map.TryGetValue(h, out var s))
            return s;
        s = Encoders.Utf8Encoder.GetString(bytes);
        _map[h] = s;
        return s;
    }

    private void AddPredefined(string stringToCache)
    {
        var bytes = Encoders.Utf8Encoder.GetBytes(stringToCache);
        uint h = Fnv1a32(bytes); // Changed from ulong
        _map[h] = stringToCache;
    }

    private static uint Fnv1a32(ReadOnlySpan<byte> data)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        uint h = offset;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= prime;
        }
        return h;
    }
}

public sealed class FastHashStringCache64
{
    private readonly Dictionary<ulong, string> _map;

    public FastHashStringCache64(List<string>? preCacheableStrings, int capacity = 256)
    {
        _map = new Dictionary<ulong, string>(capacity);

        if (preCacheableStrings is not null)
        {
            foreach (var preCacheableString in preCacheableStrings)
            {
                AddPredefined(preCacheableString);
            }
        }
    }

    public FastHashStringCache64(int capacity = 256)
    {
        _map = new Dictionary<ulong, string>(capacity);
    }

    public string GetOrAdd(ReadOnlySpan<byte> bytes)
    {
        ulong h = Fnva64(bytes);                // or xxHash64, etc.
        if (_map.TryGetValue(h, out var s))
            return s;                           // may be a false hit if collision

        s = Encoders.Utf8Encoder.GetString(bytes);     // alloc once per (colliding) hash
        _map[h] = s;                            // last-wins policy
        return s;
    }

    private void AddPredefined(string stringToCache)
    {
        var bytes = Encoders.Utf8Encoder.GetBytes(stringToCache); // ASCII safe for methods
        ulong h = Fnva64(bytes);
        _map[h] = stringToCache;
    }

    // FNV-1a 64-bit
    private static ulong Fnva64(ReadOnlySpan<byte> data)
    {
        const ulong off = 14695981039346656037UL;  // Fixed
        const ulong prime = 1099511628211UL;
        ulong h = off;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= prime;
        }
        return h;
    }
}

public class Encoders
{
    public static Encoding Utf8Encoder = Encoding.UTF8;
}
using Microsoft.Extensions.ObjectPool;
using Microsoft.VisualBasic;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
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
            new StreamPipeReaderOptions(MemoryPool<byte>.Shared, leaveOpen: false, bufferSize: 4096, minimumReadSize: 1));

        context.Writer = PipeWriter.Create(stream,
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: false));

        try
        {
            // Loop to handle multiple requests on the same connection (keep-alive)
            /*
            while (await ReadRequestHeaders(context))
            {
                //_ = ProcessRequest(pipeline, context);

                // Invoke user-defined middleware pipeline
                await pipeline(context);

                // Clear context state for reuse in the next request (on the same socket)
                context.Clear();
            }
            */

            while (true)
            {
                var readResult = await context.Reader.ReadAsync();
                var buffer = readResult.Buffer;

                var isCompleted = readResult.IsCompleted;

                if (buffer.IsEmpty && isCompleted)
                {
                    return;
                }

                // Handles one or more complete HTTP requests in the buffer
                await HandleRequestsAsync(buffer, context, pipeline, isCompleted);

                //await pipeline(context);
                //context.Clear();

                //HandleRequests(ref buffer, context, pipeline, isCompleted);
                //await context.Writer.FlushAsync();
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

    private static ReadOnlySpan<byte> _plaintextPreamble =>
        "HTTP/1.1 200 OK\r\n"u8 +
        "Server: K\r\n"u8 +
        "Content-Type: text/plain\r\n"u8 +
        "Content-Length: 13\r\n\r\n"u8;
    private static ReadOnlySpan<byte> _plainTextBody => "Hello, World!\r\n"u8;
    private static void PlainText(ref BufferWriter<WriterAdapter> writer)
    {
        writer.Write(_plaintextPreamble);

        // Date header
        //writer.Write(DateHeader.HeaderBytes);

        // Body
        writer.Write(_plainTextBody);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BufferWriter<WriterAdapter> GetWriter(PipeWriter pipeWriter, int sizeHint)
        => new(new(pipeWriter), sizeHint);

    private async Task ProcessRequest(Func<TContext, Task> pipeline, TContext context)
    {
        // Invoke user-defined middleware pipeline
        await pipeline(context);
        // Clear context state for reuse in the next request (on the same socket)
        context.Clear();
    }

    private async Task<bool> HandleRequestsAsync(
        ReadOnlySequence<byte> buffer,
        TContext context,
        Func<TContext, Task> pipeline,
        bool isCompleted)
    {
        var reader = new SequenceReader<byte>(buffer);

        while (true)
        {
            var requestReceived = ReadRequestHeaders2(context, ref reader, ref buffer, isCompleted);
            //var requestReceived = ReadRequestHeadersSlim(context, ref buffer, isCompleted);


            if (requestReceived)
            {
                context.Reader.AdvanceTo(reader.Position);
            }
            else
            {
                // Incomplete request, wait for more data
                break;
            }

            await pipeline(context);
            context.Clear();
            //PlainText(ref writer);

            //if (!reader.End)
            //{
                // More input data to parse
            //    continue;
            //}

            // No more input or incomplete data, Advance the Reader
            //context.Reader.AdvanceTo(reader.Position, buffer.End);
            break;
        }

        //writer.Commit();
        return true;
    }
    private bool HandleRequests(
        ref ReadOnlySequence<byte> buffer, 
        TContext context, 
        Func<TContext, Task> pipeline,
        bool isCompleted)
    {
        var reader = new SequenceReader<byte>(buffer);
        var writer = GetWriter(context.Writer, sizeHint: 160 * 16); // 160*16 is for Plaintext, for Json 160 would be enough

        while (true)
        {
            var requestReceived = ReadRequestHeaders2(context, ref reader, ref buffer, isCompleted);
            //var requestReceived = ReadRequestHeadersSlim(context, ref buffer, isCompleted);

            if (requestReceived)
            {
                context.Reader.AdvanceTo(reader.Position);
            }
            else
            {
                // Incomplete request, wait for more data
                break;
            }

            //_ = ProcessRequest(pipeline, context);
            PlainText(ref writer);

            if (!reader.End)
            {
                // More input data to parse
                continue;
            }

            // No more input or incomplete data, Advance the Reader
            //context.Reader.AdvanceTo(reader.Position, buffer.End);
            break;
        }

        writer.Commit();
        return true;
    }
    public static async Task<bool> ReadRequestHeaders(TContext context)
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
                                     Encoders.Utf8Encoder.GetString(pair[(equalsIndex + 1)..]));
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
    public static bool ReadRequestHeadersSlim(TContext context, ref ReadOnlySequence<byte> buffer, bool isCompleted)
    {
        var reader = context.Reader;

        while (true)
        {
            if (buffer.Length == 0 || isCompleted)
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

                    var firstSpace = firstHeader.IndexOf(Bytes.Space);
                    if (firstSpace == -1)
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
                                     Encoders.Utf8Encoder.GetString(pair[(equalsIndex + 1)..]));
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

    private static readonly byte[] CRLF = "\r\n"u8.ToArray();
    private static readonly byte[] CRLFCRLF = "\r\n\r\n"u8.ToArray();

    public static bool ReadRequestHeaders2(
        TContext context, 
        ref SequenceReader<byte> sequenceReader,
        ref ReadOnlySequence<byte> buffer,
        bool isCompleted)
    {
        //var reader = context.Reader;

        if (buffer.Length == 0 || isCompleted)
            throw new IOException("Client disconnected");

        // Look for end of headers first
        if (!sequenceReader.TryReadTo(out ReadOnlySequence<byte> headerSequence, CRLFCRLF))
        {
            // Headers not complete, advance to end and continue reading
            //reader.AdvanceTo(buffer.Start, buffer.End);
            //continue;
            return false;
        }

        // Parse the complete headers
        var headerReader = new SequenceReader<byte>(headerSequence);

        // Parse request line (method, path, version)
        if (!ParseRequestLine(ref headerReader, context.Request))
        {
            throw new InvalidOperationException("Invalid request line");
        }

        // Parse remaining headers
        while (!headerReader.End)
        {
            if (!headerReader.TryReadTo(out ReadOnlySequence<byte> headerLine, CRLF))
                break;

            if (headerLine.Length == 0)
                break; // Empty line indicates end of headers

            ParseHeaderLine(headerLine, context.Request);
        }

        // Advance past the headers in the pipe
        //reader.AdvanceTo(sequenceReader.Position);
        return true;
        
    }

    private static bool ParseRequestLine(ref SequenceReader<byte> reader, IExpressRequest request)
    {
        // Read method
        if (!reader.TryReadTo(out ReadOnlySequence<byte> methodSequence, (byte)' '))
            return false;

        request.HttpMethod = PreCachedHttpMethods.GetOrAdd(methodSequence.ToSpan());

        // Read URL/path
        if (!reader.TryReadTo(out ReadOnlySequence<byte> urlSequence, (byte)' '))
            return false;

        ParseUrl(urlSequence, request);

        // Skip HTTP version (read to end of line)
        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, CRLF))
            return false;

        return true;
    }
    private static void ParseUrl(ReadOnlySequence<byte> urlSequence, IExpressRequest request)
    {
        var urlSpan = urlSequence.ToSpan();
        var queryStart = urlSpan.IndexOf((byte)'?');

        if (queryStart != -1)
        {
            // URL has query parameters
            var routeSpan = urlSpan[..queryStart];
            request.Route = CachedRoutes.GetOrAdd(routeSpan);

            // Parse query parameters
            ParseQueryParameters(urlSpan[(queryStart + 1)..], request);
        }
        else
        {
            // Simple URL without query parameters  
            request.Route = CachedRoutes.GetOrAdd(urlSpan);
        }
    }
    private static void ParseQueryParameters(ReadOnlySpan<byte> querySpan, IExpressRequest request)
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

            var key = CachedQueryKeys.GetOrAdd(pair[..equalsIndex]);
            var value = Encoding.UTF8.GetString(pair[(equalsIndex + 1)..]);

            request.QueryParameters?.TryAdd(key, value);
        }
    }
    private static void ParseHeaderLine(ReadOnlySequence<byte> headerLine, IExpressRequest request)
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
            PreCachedHeaderKeys.GetOrAdd(headerKey),
            PreCachedHeaderValues.GetOrAdd(headerValue));
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

// Extension method for SequenceReader to convert to span efficiently
public static class SequenceExtensions
{
    public static ReadOnlySpan<byte> ToSpan(this ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
            return sequence.FirstSpan;

        // Multi-segment - need to copy to contiguous memory
        // For HTTP headers this should be rare, but handle it
        return sequence.ToArray();
    }
}
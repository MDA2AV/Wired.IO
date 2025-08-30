using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.Collections;
using System.Diagnostics.Metrics;
using System.IO.Pipelines;
using System.Text;
using Wired.IO.Http11.Context;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.HttpExpress;

public class WiredHttpExpress<TContext> : IHttpHandler<TContext>
    where TContext : Http11Context, new()
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
            while (await ExtractHeadersAsync(context))
            {
                // Parse the HTTP request line (e.g. "GET /index.html HTTP/1.1") from the first header line
                IEnumerator enumerator = context.Request.Headers.GetEnumerator();
                enumerator.MoveNext();

                ParseHttpRequestLine(
                    ((KeyValuePair<string, string>)enumerator.Current).Value,
                    context.Request);

                // Invoke user-defined middleware pipeline
                await pipeline(context);

                // Clear context state for reuse in the next request (on the same socket)
                context.Clear();

                // If the client indicated "Connection: close", exit the loop
                if (context.Request.ConnectionType is not ConnectionType.KeepAlive)
                    break;
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

    public static async ValueTask<Span<char>> ExtractRouteAsync(TContext context)
    {
        var reader = context.Reader;

        while (true)
        {
            var result = await reader.ReadAsync(context.CancellationToken);
            var buffer = result.Buffer;

            if (buffer.Length == 0)
            {
                throw new IOException("Client disconnected");
            }

            if (buffer.IsSingleSegment)
            {

            }
            else
            {

            }

            if (PipeReaderUtilities.TryAdvanceTo(new SequenceReader<byte>(buffer), "\r\n\r\n"u8, out var position))
            {
                var headerBytes = buffer.Slice(0, position);

                // Decode directly into stack memory
                var byteLength = (int)headerBytes.Length;
                var byteSpan = byteLength <= 1024 ? stackalloc byte[byteLength] : new byte[byteLength];
                headerBytes.CopyTo(byteSpan);

                var charCount = Encoding.UTF8.GetCharCount(byteSpan);
                var charSpan = charCount <= 1024 ? stackalloc char[charCount] : new char[charCount];
                Encoding.UTF8.GetChars(byteSpan, charSpan);

                // Advance past the headers
                reader.AdvanceTo(position);

                // Parse headers
                var lineEnd = charSpan.IndexOf("\r\n");

                if (lineEnd == -1)
                    break;

                var line = charSpan[..lineEnd];

                context.Request.Headers.TryAdd(":Request-Line", new string(line));

                return true;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
                return false;
        }
    }

    static async ValueTask<ReadOnlySequence<byte>> ReadHeadersAsync(PipeReader reader)
    {
        ReadOnlySequence<byte> headersWithDelimiter = default;

        while (true)
        {
            var result = await reader.ReadAsync();
            var buffer = result.Buffer;

            // Try to find CRLFCRLF across segments
            var sr = new SequenceReader<byte>(buffer);
            if (sr.TryReadTo(out ReadOnlySequence<byte> before, "\r\n\r\n"u8, advancePastDelimiter: true))
            {
                // sr.Position is now AFTER the delimiter
                var after = sr.Position;

                // You want headers INCLUDING the delimiter:
                headersWithDelimiter = buffer.Slice(0, after);

                // Consume through the delimiter
                reader.AdvanceTo(after, after);
                return headersWithDelimiter;
            }

            // Not found yet: preserve a tail of (delimiter.Length - 1) bytes
            // so a split delimiter can complete on the next read.
            const int keep = 4 - 1; // for "\r\n\r\n"
            if (buffer.Length > keep)
            {
                var consumeTo = buffer.GetPosition(buffer.Length - keep);
                reader.AdvanceTo(consumeTo, buffer.End);
            }
            else
            {
                // Too little data to safely consume anything
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
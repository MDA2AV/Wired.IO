using System.Buffers;
using Microsoft.Extensions.ObjectPool;
using Glyph11;
using Glyph11.Parser;
using Glyph11.Parser.UltraHardened;
using Glyph11.Validation;
using ioxide;
using Wired.IO.Handlers.Http11Oxide.Context;
using Wired.IO.Transport.Oxide;

namespace Wired.IO.Handlers.Http11Oxide;

/// <summary>The default ioxide + Glyph11 tier handler.</summary>
public sealed class WiredHttp11Oxide : WiredHttp11Oxide<Http11OxideContext>;

/// <summary>
/// Per-connection loop for the ioxide tier: read the recv rings through ioxide's zero-copy
/// <see cref="ConnectionPipeReader"/> (a <see cref="System.IO.Pipelines.PipeReader"/> over the
/// provided-buffer rings — no copy) → parse each request with Glyph11 (<see cref="UltraHardenedParser"/>)
/// into <c>ctx.Binary</c> → run Wired's pipeline (middleware + routing + endpoint + DI) → write the
/// response into the slab → flush. Pipelined and chunked. The framework above the parse is 100% Wired's.
/// <para>
/// No carry copy: a request held in a single recv slice (the common case) is parsed straight off the
/// ring memory. Only a request that spans recv buffers is linearized — and that is exactly the case
/// where Glyph11 itself linearizes today; a true segmented Glyph11 parser removes even that copy.
/// </para>
/// </summary>
public partial class WiredHttp11Oxide<TContext> : IOxideHttpHandler<TContext>
    where TContext : Http11OxideContext, new()
{
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new Policy(), 4096 * 4);

    private sealed class Policy : PooledObjectPolicy<TContext>
    {
        public override TContext Create() => new();
        public override bool Return(TContext context) => true;
    }

    private static readonly ParserLimits Limits = ParserLimits.Default;

    public async Task HandleClientAsync(Connection connection, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        var ctx = ContextPool.Get();
        ctx.Connection = connection;
        ctx.CancellationToken = stoppingToken;

        // One zero-copy reader per connection: ReadAsync hands back a ReadOnlySequence over the held
        // recv ring slices; AdvanceTo returns the consumed buffers to the ring.
        var reader = new ConnectionPipeReader(connection);

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(stoppingToken);
                var buffer = result.Buffer;

                // Serve every complete request currently held (pipelining + fragmentation); returns the
                // byte offset of the first incomplete request, which stays held for the next read.
                var consumed = await ServeAll(ctx, pipeline, buffer);

                // Release consumed ring buffers; mark the remainder examined so the next ReadAsync parks
                // for new bytes instead of handing back the same partial request.
                reader.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ioxide] handler fd={connection.ClientFd}: {ex.Message}");
        }
        finally
        {
            reader.Complete();
            connection.DecRef();
            ContextPool.Return(ctx);
        }
    }

    // Parse + run + answer every complete request held in the zero-copy sequence; returns the bytes
    // consumed (the start of the first incomplete request, left held for the next read).
    private static async ValueTask<long> ServeAll(TContext ctx, Func<TContext, Task> pipeline, ReadOnlySequence<byte> buffer)
    {
        long consumed = 0;
        var total = buffer.Length;

        while (consumed < total)
        {
            var rem = buffer.Slice(consumed);

            // Contiguous view of the remaining bytes: zero-copy when it's one ring slice (the common
            // case), linearized only when a request spans recv buffers (rare — and exactly where Glyph11
            // would ToArray anyway). A segmented Glyph11 parser removes this copy; flagged as follow-up.
            ReadOnlyMemory<byte> mem;
            if (rem.IsSingleSegment)
                mem = rem.First;
            else
                mem = rem.ToArray();

            ctx.Binary.Clear();
            var header = mem;

            try
            {
                if (!UltraHardenedParser.TryExtractFullHeaderROM(ref header, ctx.Binary, in Limits, out var bytesRead))
                    break; // header not complete — wait for the next read

                var bodyStart = bytesRead + 1;
                if (!TryAdvancePastBody(ctx, mem.Span, bodyStart, out var requestLen, out var needMore))
                {
                    if (needMore)
                        break; // body not complete — wait for the next read
                    WriteSimple(ctx.Connection, "400 Bad Request"u8);
                    await ctx.Connection.FlushAsync();
                    consumed = total;
                    break;
                }

                // Materialize the keys Wired's router matches on, then run Wired's pipeline
                // (root + group middleware, route match, DI scope, endpoint) over this context.
                ctx.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(ctx.Binary.Method.Span);
                ctx.Request.Route = CachedData.CachedRoutes.GetOrAdd(ctx.Binary.Path.Span);

                await pipeline(ctx);

                if (ctx.Response is { } resp && resp.IsActive())
                {
                    WriteStatusLine(ctx.Connection, resp.Status);
                    WriteHeaders(ctx);
                    resp.ContentHandler();
                }
                await ctx.Connection.FlushAsync();
                ctx.Response?.Clear();

                consumed += requestLen;
            }
            catch (HttpParseException ex)
            {
                WriteSimple(ctx.Connection, ex.StatusCode == 431 ? "431 Request Header Fields Too Large"u8 : "400 Bad Request"u8);
                await ctx.Connection.FlushAsync();
                consumed = total;
                break;
            }
        }

        return consumed;
    }

    // Total length (header + body) of the request whose header ends at bodyStart, over the contiguous
    // view <paramref name="mem"/>. needMore = the body isn't fully buffered yet. The body bytes are not
    // yet surfaced on the context (a follow-up); this slice frames past them for pipelining.
    private static bool TryAdvancePastBody(TContext ctx, ReadOnlySpan<byte> mem, int bodyStart, out int requestLen, out bool needMore)
    {
        needMore = false;
        requestLen = bodyStart;
        var framing = BodyFramingDetector.DetectBodyFraming(ctx.Binary);

        switch (framing.Framing)
        {
            case BodyFraming.None:
                return true;
            case BodyFraming.ContentLength:
                var cl = framing.ContentLength;
                if (mem.Length - bodyStart < cl) { needMore = true; return false; }
                requestLen = bodyStart + (int)cl;
                return true;
            case BodyFraming.Chunked:
                var decoder = new ChunkedBodyStream();
                var pos = bodyStart;
                while (true)
                {
                    var r = decoder.TryReadChunk(mem[pos..], out var consumed, out _, out _);
                    switch (r)
                    {
                        case ChunkResult.Chunk: pos += consumed; continue;
                        case ChunkResult.Completed: requestLen = pos + consumed; return true;
                        case ChunkResult.NeedMoreData: needMore = true; return false;
                        default: return false;
                    }
                }
            default:
                return true;
        }
    }
}

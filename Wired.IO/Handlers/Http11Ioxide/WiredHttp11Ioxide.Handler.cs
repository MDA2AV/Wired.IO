using Microsoft.Extensions.ObjectPool;
using Glyph11;
using Glyph11.Parser;
using Glyph11.Parser.UltraHardened;
using Glyph11.Validation;
using ioxide;
using Wired.IO.Handlers.Http11Ioxide.Context;
using Wired.IO.Transport.Ioxide;

namespace Wired.IO.Handlers.Http11Ioxide;

/// <summary>The default ioxide + Glyph11 tier handler.</summary>
public sealed class WiredHttp11Ioxide : WiredHttp11Ioxide<Http11IoxideContext>;

/// <summary>
/// Per-connection loop for the ioxide tier: recv off the io_uring rings → carry buffer → Glyph11
/// (<see cref="UltraHardenedParser"/>) into <c>ctx.Binary</c> → run Wired's pipeline (middleware +
/// routing + endpoint + DI) → write the response into the slab → flush. No Stream, no Pipe. Pipelined
/// and chunked. The framework above the parse is 100% Wired's.
/// </summary>
public partial class WiredHttp11Ioxide<TContext> : IIoxideHttpHandler<TContext>
    where TContext : Http11IoxideContext, new()
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
        var carryLen = 0;

        try
        {
            while (true)
            {
                var snap = await connection.ReadAsync();
                carryLen = Drain(connection, snap, ctx, carryLen);

                var closed = snap.IsClosed;
                if (!closed)
                    connection.ResetRead();

                carryLen = await ServeAll(ctx, pipeline, carryLen);

                if (closed)
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ioxide] handler fd={connection.ClientFd}: {ex.Message}");
        }
        finally
        {
            connection.DecRef();
            ContextPool.Return(ctx);
        }
    }

    // Copy each received io_uring slice into the carry buffer, then hand the ring buffer back.
    private static unsafe int Drain(Connection conn, in RecvSnapshot snap, TContext ctx, int carryLen)
    {
        while (conn.TryGetItem(snap, out var item))
        {
            if (item.HasBuffer)
            {
                var slice = item.AsSpan();
                if (ctx.Carry.Length < carryLen + slice.Length)
                    Array.Resize(ref ctx.Carry, Math.Max(carryLen + slice.Length, ctx.Carry.Length * 2));
                slice.CopyTo(ctx.Carry.AsSpan(carryLen));
                carryLen += slice.Length;
            }
            conn.ReturnBuffer(in item);
        }
        return carryLen;
    }

    // Parse + run + answer every complete request in the carry; returns the leftover (partial) length.
    private static async ValueTask<int> ServeAll(TContext ctx, Func<TContext, Task> pipeline, int carryLen)
    {
        var carry = ctx.Carry;
        var offset = 0;

        while (offset < carryLen)
        {
            ctx.Binary.Clear();
            ReadOnlyMemory<byte> mem = carry.AsMemory(offset, carryLen - offset);

            try
            {
                if (!UltraHardenedParser.TryExtractFullHeaderROM(ref mem, ctx.Binary, in Limits, out var bytesRead))
                    break; // header not complete — wait for the next read

                var bodyStart = offset + bytesRead + 1;
                if (!TryAdvancePastBody(ctx, carryLen, bodyStart, out var nextOffset, out var needMore))
                {
                    if (needMore)
                        break;
                    WriteSimple(ctx.Connection, "400 Bad Request"u8);
                    await ctx.Connection.FlushAsync();
                    offset = carryLen;
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

                offset = nextOffset;
            }
            catch (HttpParseException ex)
            {
                WriteSimple(ctx.Connection, ex.StatusCode == 431 ? "431 Request Header Fields Too Large"u8 : "400 Bad Request"u8);
                await ctx.Connection.FlushAsync();
                offset = carryLen;
                break;
            }
        }

        var leftover = carryLen - offset;
        if (offset > 0 && leftover > 0)
            Array.Copy(carry, offset, carry, 0, leftover);
        return leftover;
    }

    // Locate the end of this request's body (for pipelining). The body bytes live in ctx.Carry; a
    // body-exposing accessor on the context is a follow-up (this slice serves GET-shaped profiles).
    private static bool TryAdvancePastBody(TContext ctx, int carryLen, int bodyStart, out int nextOffset, out bool needMore)
    {
        needMore = false;
        nextOffset = bodyStart;
        var framing = BodyFramingDetector.DetectBodyFraming(ctx.Binary);

        switch (framing.Framing)
        {
            case BodyFraming.None:
                return true;
            case BodyFraming.ContentLength:
                var cl = framing.ContentLength;
                if (carryLen - bodyStart < cl) { needMore = true; return false; }
                nextOffset = bodyStart + (int)cl;
                return true;
            case BodyFraming.Chunked:
                var decoder = new ChunkedBodyStream();
                var pos = bodyStart;
                while (true)
                {
                    var r = decoder.TryReadChunk(ctx.Carry.AsSpan(pos, carryLen - pos), out var consumed, out _, out _);
                    switch (r)
                    {
                        case ChunkResult.Chunk: pos += consumed; continue;
                        case ChunkResult.Completed: nextOffset = pos + consumed; return true;
                        case ChunkResult.NeedMoreData: needMore = true; return false;
                        default: return false;
                    }
                }
            default:
                return true;
        }
    }
}

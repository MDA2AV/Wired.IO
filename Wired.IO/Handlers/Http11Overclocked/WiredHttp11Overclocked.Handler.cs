using System.Buffers;
using Microsoft.Extensions.ObjectPool;
using URocket.Connection;
using URocket.Utils.UnmanagedMemoryManager;
using Wired.IO.Handlers.Http11Overclocked.Context;
using Wired.IO.Protocol.Writers;
using Wired.IO.Transport.Rocket;

namespace Wired.IO.Handlers.Http11Overclocked;

// This Handler does not support pipelined requests

public sealed class WiredHttp11Overclocked : WiredHttp11Rocket<Http11OverclockedContext> { }

public partial class WiredHttp11Rocket<TContext> : IRocketHttpHandler<TContext>
    where TContext : Http11OverclockedContext, new()
{
    /// <summary>
    /// Object pool used to recycle request contexts for reduced allocations and improved performance.
    /// </summary>
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new PipelinedContextPoolPolicy(), 4096 * 4);

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
            // Overclocked context does not need to be cleared
            //context.Clear(); // User-defined reset method to clean internal state.
            return true;
        }
    }

    public async Task HandleClientAsync(Connection connection, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        // Rent a context object from the pool
        var context = ContextPool.Get();
        context.Connection = connection;
        
        try
        {
            await ProcessRequestsAsync(context, pipeline);
        }
        catch(Exception e)
        {
            Console.WriteLine(e.Message);
            // TODO Check the CancellationTokenSource Impl @ Express Handler
        }
        finally
        {
            // Return context to pool for reuse
            ContextPool.Return(context);
        }
    }

    private async Task ProcessRequestsAsync(TContext context, Func<TContext, Task> pipeline)
    {
        while (true)
        {
            var result = await context.Connection.ReadAsync(); // Read data from the wire
            if (result.IsClosed)
                break;
            
            var rings = context.Connection.PeekAllSnapshotRingsAsUnmanagedMemory(result);
            var sequence = rings.ToReadOnlySequence();

            if (sequence.IsSingleSegment)
            {
                // Single segment, fast path, use span based operations
                var span = sequence.FirstSpan;

                var idx = span.IndexOf(CrlfCrlf);
                if (idx == -1) /* Not enough data */ continue;
                
                // Full header is present
                var firstLineEnd = span.IndexOf(Crlf);
                var firstHeader = span[..firstLineEnd];
                
                var firstSpace = firstHeader.IndexOf(Space);
                if (firstSpace == -1) throw new InvalidOperationException("Invalid request line");
                
                context.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(firstHeader[..firstSpace]);
                
                var secondSpaceRelative = firstHeader[(firstSpace + 1)..].IndexOf(Space);
                if (secondSpaceRelative == -1) throw new InvalidOperationException("Invalid request line");
                
                var secondSpace = firstSpace + secondSpaceRelative + 1;
                var url = firstHeader[(firstSpace + 1)..secondSpace];
                
                // Url is same as route
                context.Request.Route = CachedData.CachedRoutes.GetOrAdd(url);
                
                // No more parsing, this handler now delegates parsing control to the request pipeline.
                // We could also add logic to check whether a body is present and wait for more data if body is not fully received. - Not needed for this benchmark
            }
            else
            {
                var reader = new SequenceReader<byte>(sequence);
                if (!reader.TryReadTo(out ReadOnlySequence<byte> headers, CrlfCrlf)) /* Not enough data */ continue;
    
                // Full header is present
                
                // Extract the Http method
                if(!reader.TryReadTo(out ReadOnlySequence<byte> methodSequence, Space)) /* Not enough data */ continue;
                
                context.Request.HttpMethod = CachedData.PreCachedHttpMethods.GetOrAdd(methodSequence.ToSpan());
                
                // Read URL/path
                if (!reader.TryReadTo(out ReadOnlySequence<byte> urlSequence, Space)) /* Not enough data */ continue;
                
                // Url is same as route
                context.Request.Route = CachedData.CachedRoutes.GetOrAdd(urlSequence.ToSpan());
                
                // No more parsing, this handler now delegates parsing control to the request pipeline.
                // We could also add logic to check whether a body is present and wait for more data if body is not fully received. - Not needed for this benchmark
            }
            
            // Execute request pipeline
            await pipeline(context);

            if (context.Response is not null && context.Response.IsActive())
            {
                WriteStatusLine(context.Connection, context.Response.Status);
                WriteHeaders(context);
                context.Response.ContentHandler();
            }

            await context.Connection.FlushAsync();
            
            // Dequeue and return rings to the kernel
            for(int i = 0; i < rings.Length; i++)
                context.Connection.GetRing();
            foreach (var ring in rings)
                context.Connection.ReturnRing(ring.BufferId);
            
            // Signal we are ready for a new read
            context.Connection.ResetRead();
        }
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
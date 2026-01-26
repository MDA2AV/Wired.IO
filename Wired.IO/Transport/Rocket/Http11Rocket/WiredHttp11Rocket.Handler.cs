using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.ObjectPool;
using URocket.Connection;
using URocket.Utils;
using URocket.Utils.UnmanagedMemoryManager;
using Wired.IO.Transport.Rocket.Http11Rocket.Context;

namespace Wired.IO.Transport.Rocket.Http11Rocket;

// Public non-generic facade
public sealed class WiredHttp11Rocket : WiredHttp11Rocket<Http11RocketContext> { }

public partial class WiredHttp11Rocket<TContext> : IRocketHttpHandler<TContext>
    where TContext : Http11RocketContext, new()
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
            context.Clear(); // User-defined reset method to clean internal state.
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
            await HandleConnectionAsync(context, pipeline);
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
    
    internal static async Task HandleConnectionAsync(TContext context, Func<TContext, Task> pipeline)
    {
        while (true)
        {
            var result = await context.Connection.ReadAsync();
            if (result.IsClosed)
                break;
            
            // Get all ring buffers data
            var rings = context.Connection.GetAllSnapshotRingsAsUnmanagedMemory(result);
            // Create a ReadOnlySequence<byte> to easily slice the data
            var sequence = rings.ToReadOnlySequence();
            
            // Process received data...
            
            context.Request.HttpMethod = "GET";
            context.Request.Route = "/route";
            await pipeline(context);
            
            // Return rings to the kernel
            foreach (var ring in rings)
                context.Connection.ReturnRing(ring.BufferId);
            
            // Write the response
            var msg =
                "HTTP/1.1 200 OK\r\nContent-Length: 13\r\nContent-Type: text/plain\r\n\r\nHello, World!"u8;

            // Building an UnmanagedMemoryManager wrapping the msg, this step has no data allocation
            // however msg must be fixed/pinned because the engine reactor's needs to pass a byte* to liburing
            unsafe
            {
                var unmanagedMemory = new UnmanagedMemoryManager(
                    (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(msg)),
                    msg.Length,
                    false); // Setting freeable to false signaling that this unmanaged memory should not be freed because it comes from an u8 literal
                
                if (!context.Connection.Write(new WriteItem(unmanagedMemory, context.Connection.ClientFd)))
                    throw new InvalidOperationException("Failed to write response");
            }
            
            // Signal that written data can be flushed
            context.Connection.Flush();
            // Signal we are ready for a new read
            context.Connection.ResetRead();
        }
    }
}
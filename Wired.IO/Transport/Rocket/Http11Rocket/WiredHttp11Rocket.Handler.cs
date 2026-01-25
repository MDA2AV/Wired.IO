using Microsoft.Extensions.ObjectPool;
using URocket.Connection;
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
    }
}
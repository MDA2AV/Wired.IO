using Microsoft.Extensions.ObjectPool;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol;
using System.Reflection;
using Wired.IO.Http11.Context;

namespace Wired.IO.Http11;

// Public non-generic facade
public sealed class WiredHttp11(IHandlerArgs args) : WiredHttp11<Http11Context>(args)
{
}

/// <summary>
/// HTTP/1.1 handler implementation for processing incoming requests using either
/// blocking or non-blocking strategies over a single stream.
/// </summary>
/// <typeparam name="TContext">
/// The context type used per request. Must implement <see cref="IContext"/> and have a public parameterless constructor.
/// </typeparam>
/// <param name="args">Configuration arguments for the handler.</param>
public partial class WiredHttp11<TContext>(IHandlerArgs args) : IHttpHandler<TContext>
    where TContext : Http11Context, new()
{
    /// <summary>
    /// Object pool used to recycle request contexts for reduced allocations and improved performance.
    /// </summary>
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new PipelinedContextPoolPolicy(), 8192);

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

    /// <summary>
    /// Handles an HTTP/1.1 client connection using the selected handler mode (blocking or non-blocking).
    /// </summary>
    /// <param name="stream">The stream representing the client connection.</param>
    /// <param name="pipeline">The middleware/request handling pipeline.</param>
    /// <param name="stoppingToken">A cancellation token used to terminate the operation.</param>
    public async Task HandleClientAsync(Stream stream, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        if (HandlerType == Http11HandlerType.Blocking)
        {
            await HandleBlocking(stream, pipeline);
        }
        else
        {
            await HandleNonBlocking(stream, pipeline);
        }
    }
}

/// <summary>
/// Configuration record used to initialize the HTTP/1.1 handler.
/// </summary>
/// <param name="UseResources">Whether to serve embedded resources (e.g. static files).</param>
/// <param name="ResourcesPath">The virtual path used to locate resources (e.g. "/static").</param>
/// <param name="ResourcesAssembly">The assembly containing embedded resources.</param>
/// <param name="HandlerType">The handler mode to use (blocking or non-blocking).</param>
public record Http11HandlerArgs(
    bool UseResources,
    string ResourcesPath,
    Assembly ResourcesAssembly,
    Http11HandlerType HandlerType = Http11HandlerType.Blocking
) : IHandlerArgs;

/// <summary>
/// Defines the mode in which the HTTP/1.1 handler operates.
/// </summary>
public enum Http11HandlerType
{
    /// <summary>
    /// Handles each request sequentially, waiting for each response to complete before accepting a new one.
    /// </summary>
    Blocking,

    /// <summary>
    /// Handles multiple pipelined requests concurrently over the same connection.
    /// </summary>
    NonBlocking
}
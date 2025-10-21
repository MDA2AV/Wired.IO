using Wired.IO.Builder;
using Wired.IO.Http11Express.Context;
using Wired.IO.Http11Express.StaticHandlers;


namespace Wired.IO.Http11Express.BuilderExtensions;

public static class FlowControlEndpoints
{
    /// <summary>
    /// Adds a default <c>404 Not Found</c> endpoint to the application pipeline.
    /// </summary>
    /// <remarks>
    /// This endpoint is automatically invoked when no route matches the incoming request.
    /// It responds synchronously with a plain-text <c>"Endpoint not found"</c> message,
    /// using a pre-bound, non-allocating delegate for maximum performance.
    ///
    /// The endpoint is registered under the internal flow-control key <c>"NotFound"</c>.
    /// </remarks>
    /// <param name="builder">
    /// The current <see cref="Builder{THandler, TContext}"/> instance used to configure the app.
    /// </param>
    /// <returns>
    /// The same <paramref name="builder"/> instance for fluent chaining.
    /// </returns>
    public static Builder<WiredHttp11Express, Http11ExpressContext> AddNotFoundEndpoint(
        this Builder<WiredHttp11Express, Http11ExpressContext> builder)
    {
        builder.MapFlowControl("NotFound", FlowControl.CreateEndpointNotFoundHandler());

        return builder;
    }
    // Overload for WiredHttp11Express<TContext>
    public static Builder<WiredHttp11Express<TContext>, TContext> AddNotFoundEndpoint<TContext>(
        this Builder<WiredHttp11Express<TContext>, TContext> builder)
        where TContext : Http11ExpressContext, new()
    {
        builder.MapFlowControl("NotFound", FlowControl.CreateEndpointNotFoundHandler());

        return builder;
    }
}
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.Handlers.Http11Express.Context;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Handlers.Http11Express.StaticHandlers;

/// <summary>
/// Provides prebuilt lightweight response handlers for exceptional or control flow
/// scenarios within the HTTP pipeline (e.g., 404 endpoint not found).
/// 
/// All handlers returned here are fully static and synchronous, avoiding closures
/// and heap allocations.
/// </summary>
internal static class FlowControl
{
    /// <summary>
    /// Creates a statically bound request handler that responds with a 404 Not Found message.
    /// </summary>
    /// <returns>
    /// A non-allocating <see cref="Func{Http11ExpressContext, Task}"/> delegate suitable for direct
    /// assignment as a request handler for unregistered endpoints.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The returned delegate:
    /// <list type="bullet">
    ///   <item>Writes a plain-text "Endpoint not found" response body (18 bytes).</item>
    ///   <item>Sets the HTTP status to <see cref="ResponseStatus.NotFound"/>.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static Func<Http11ExpressContext, Task> CreateEndpointNotFoundHandler()
    {
        // Return the actual static resource request handler.
        return static ctx =>
        {
            // Create a bound, non-allocating handler delegate for this resource.
            var handler = CreateBoundHandler(ctx.Writer);

            // Write the response with the correct MIME type and content length.
            ctx.Respond()
                .Status(ResponseStatus.NotFound)
                .Type("text/plain"u8)
                .Content(handler, 18);

            // All writes are synchronous; no async IO required.
            return Task.CompletedTask;
        };
    }
    
    
    /// <summary>
    /// A shared delegate reference for the static 404 response writer.
    /// </summary>
    private static readonly Action<PipeWriter> EndpointNotFoundHandler = Handler;

    /// <summary>
    /// Binds the static <see cref="EndpointNotFoundHandler"/> to a given <see cref="PipeWriter"/>
    /// without creating any closures or heap allocations.
    /// </summary>
    /// <param name="writer">The target writer to which the response will be written.</param>
    /// <returns>
    /// An <see cref="Action"/> delegate that, when invoked, writes the static 404 message
    /// to the provided <see cref="PipeWriter"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Action CreateBoundHandler(PipeWriter writer) => () => EndpointNotFoundHandler.Invoke(writer);

    /// <summary>
    /// Writes the literal text "Endpoint not found" to the provided <see cref="PipeWriter"/>.
    /// </summary>
    /// <param name="writer">The target writer for the response body.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Handler(PipeWriter writer) => writer.Write("Endpoint not found"u8);
}
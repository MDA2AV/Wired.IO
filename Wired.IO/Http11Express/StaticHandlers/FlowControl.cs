using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.App;
using Wired.IO.Http11Express.Context;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express.StaticHandlers;

internal static class FlowControl
{
    public static Func<IServiceProvider, Func<TContext, Task>> CreateEndpointNotFoundHandler<TContext>()
        where TContext : class
    {
        return static _ =>
        {
            // Return the actual static resource request handler.
            return static ctx =>
            {
                var context = Unsafe.As<Http11ExpressContext>(ctx);

                // Create a bound, non-allocating handler delegate for this resource.
                var handler = CreateBoundHandler(context.Writer);

                // Write the response with the correct MIME type and content length.
                context.Respond()
                    .Status(ResponseStatus.NotFound)
                    .Type("text/plain"u8)
                    .Content(handler, 18);

                // All writes are synchronous; no async IO required.
                return Task.CompletedTask;
            };
        };
    }

    private static readonly Action<PipeWriter> EndpointNotFoundHandler = Handler;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Action CreateBoundHandler(PipeWriter writer)
        => () => EndpointNotFoundHandler.Invoke(writer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Handler(PipeWriter writer)
    {
        writer.Write("Endpoint not found"u8);
    }
}
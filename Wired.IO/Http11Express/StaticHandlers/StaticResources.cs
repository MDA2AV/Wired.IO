using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.App;
using Wired.IO.Http11.Context;
using Wired.IO.Http11Express.Context;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express.StaticHandlers;

public static class StaticResources
{
    public static Func<IServiceProvider, Func<TContext, Task>> CreateHandler<TContext>()
        where TContext : class
    {
        return static _ =>
        {
            // resolve per-scope dependencies here if you have any
            // var dep = sp.GetRequiredService<...>();

            // return the actual request handler
            return static ctx =>
            {
                var context = Unsafe.As<Http11ExpressContext>(ctx);

                Console.WriteLine($"Serving {context.Request.Route}");

                var resource = WiredApp<Http11Context>.StaticCachedResourceFiles[context.Request.Route];

                var handler = CreateBoundHandler(context.Writer, resource);

                context.Respond()
                    .Type(MimeTypes.GetMimeType(context.Request.Route))
                    .Content(handler);

                return Task.CompletedTask; // if your write is synchronous
            };
        };
    }

    public static Action CreateBoundHandler(PipeWriter writer, ReadOnlyMemory<byte> resource) => () => StaticHandler.Invoke(writer, resource);
    private static readonly Action<PipeWriter, ReadOnlyMemory<byte>> StaticHandler = HandleFast;
    private static void HandleFast(PipeWriter writer, ReadOnlyMemory<byte> resource)
    {
        writer.Write(resource.Span);
    }
}
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.App;
using Wired.IO.Http11Express.Context;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express.StaticHandlers;

public static class StaticResources
{
    public static Func<IServiceProvider, Func<TContext, Task>> CreateStaticResourceHandler<TContext>()
        where TContext : class
    {
        Console.WriteLine("StaticResources.CreateStaticResourceHandler");

        return static _ =>
        {
            // return the actual request handler
            return static ctx =>
            {
                var context = Unsafe.As<Http11ExpressContext>(ctx);

                Console.WriteLine($"Serving {context.Request.Route}");

                var resource = WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[context.Request.Route];

                var handler = CreateBoundHandler(context.Writer, resource);

                context.Respond()
                    .Type(MimeTypes.GetMimeType(context.Request.Route))
                    .Content(handler, (ulong)resource.Length);

                return Task.CompletedTask; // if your write is synchronous
            };
        };
    }

    public static Func<IServiceProvider, Func<TContext, Task>> CreateSpaResourceHandler<TContext>()
        where TContext : class
    {
        Console.WriteLine("StaticResources.CreateSpaResourceHandler");

        return static _ =>
        {
            // return the actual request handler
            return static ctx =>
            {
                var context = Unsafe.As<Http11ExpressContext>(ctx);

                Console.WriteLine($"Serving {context.Request.Route}");

                var resource = WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[context.Request.Route];

                var handler = CreateBoundHandler(context.Writer, resource);

                context.Respond()
                    .Type(MimeTypes.GetSpaMimeType(context.Request.Route))
                    .Content(handler, (ulong)resource.Length);

                return Task.CompletedTask; // if your write is synchronous
            };
        };
    }

    private static readonly Action<PipeWriter, ReadOnlyMemory<byte>> StaticHandler = HandleFast;

    public static Action CreateBoundHandler(PipeWriter writer, ReadOnlyMemory<byte> resource) => () => StaticHandler.Invoke(writer, resource);

    private static void HandleFast(PipeWriter writer, ReadOnlyMemory<byte> resource) => writer.Write(resource.Span);
}
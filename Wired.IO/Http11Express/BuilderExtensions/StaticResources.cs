using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.App;
using Wired.IO.Builder;
using Wired.IO.Http11Express.Context;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express.BuilderExtensions;

public static class BuilderExtensions
{
    public static Builder<WiredHttp11Express, Http11ExpressContext> AddStaticResourceProvider(
        this Builder<WiredHttp11Express, Http11ExpressContext> builder,
        string route,
        Location location,
        List<Func<Http11ExpressContext, Func<Http11ExpressContext, Task>, Task>>? middlewares,
        List<string>? keys = null)
    {
        keys ??= [HttpConstants.Get];

        // Trim out the wildcard to get the base route for static files
        builder.ServeStaticFiles(route.Replace("*", ""), location);

        builder.AddManualPipeline(
            route,
            keys,

            // Pure delegate: static + only uses parameters/local variables
            static ctx =>
            {
                var routePath = ctx.Request.Route;

                if (!Path.HasExtension(routePath))
                {
                    // Not a valid resource, short-circuit

                    ctx.Respond()
                        .Status(ResponseStatus.NotFound)
                        .Type("text/plain"u8)
                        .Content("Resource does not have a valid file extension."u8);

                    return Task.CompletedTask;
                }

                // Try the cache first
                if (!WiredApp<Http11ExpressContext>.StaticCachedResourceFiles.TryGetValue(routePath, out var resource))
                {
                    // Load from source on miss
                    if (!WiredApp<Http11ExpressContext>.TryReadResource(routePath, out resource))
                    {
                        // Not found
                        ctx.Respond()
                            .Status(ResponseStatus.NotFound)
                            .Type("text/plain"u8)
                            .Content("Resource was not found."u8);

                        return Task.CompletedTask;
                    }

                    // Populate cache
                    WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[routePath] = resource;
                }

                // Serve cached (or freshly loaded) resource
                var handler = CreateBoundHandler(ctx.Writer, resource);

                ctx.Respond()
                    .Status(ResponseStatus.Ok)
                    .Type(MimeTypes.GetMimeType(routePath))
                    .Content(handler, (ulong)resource.Length);

                return Task.CompletedTask;
            },
            middlewares);

        return builder;
    }

    public static Builder<WiredHttp11Express, Http11ExpressContext> AddSpaProvider(
        this Builder<WiredHttp11Express, Http11ExpressContext> builder,
        string route,
        Location location,
        List<Func<Http11ExpressContext, Func<Http11ExpressContext, Task>, Task>>? middlewares,
        List<string>? keys = null)
    {
        keys ??= [HttpConstants.Get];

        // Trim out the wildcard to get the base route for static files
        builder.ServeSpaFiles(route.Replace("*", ""), location);

        builder.AddManualPipeline(
            route,
            keys,

            // Pure delegate: static + only uses parameters/local variables
            static ctx =>
            {
                var routePath = ctx.Request.Route;

                // Try the cache first
                if (!WiredApp<Http11ExpressContext>.StaticCachedResourceFiles.TryGetValue(routePath, out var resource))
                {
                    if (!Path.HasExtension(routePath))
                    {
                        // Short circuit to index.html for SPA routes without extensions

                        if (WiredApp<Http11ExpressContext>.TryReadFallbackSpaResource(ctx.Request.Route, out var indexHtml))
                        {
                            // Cache the resource for future requests and short circuit to static file endpoint
                            WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[ctx.Request.Route] = indexHtml;

                            ctx.Respond()
                                .Status(ResponseStatus.Ok)
                                .Type(MimeTypes.GetSpaMimeType(routePath))
                                .Content(CreateBoundHandler(ctx.Writer, indexHtml), (ulong)indexHtml.Length);
                        }

                        return Task.CompletedTask;
                    }

                    // Load from source on miss
                    if (!WiredApp<Http11ExpressContext>.TryReadResource(routePath, out resource))
                    {
                        // Not found
                        ctx.Respond()
                            .Status(ResponseStatus.NotFound)
                            .Type("text/plain"u8)
                            .Content("Resource was not found."u8);

                        return Task.CompletedTask;
                    }

                    // Populate cache
                    WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[routePath] = resource;
                }

                // Serve cached (or freshly loaded) resource
                var handler = CreateBoundHandler(ctx.Writer, resource);

                ctx.Respond()
                    .Status(ResponseStatus.Ok)
                    .Type(MimeTypes.GetSpaMimeType(routePath))
                    .Content(handler, (ulong)resource.Length);

                return Task.CompletedTask;
            },
            middlewares);

        return builder;
    }

    public static Builder<WiredHttp11Express, Http11ExpressContext> AddMpaProvider(
        this Builder<WiredHttp11Express, Http11ExpressContext> builder,
        string route,
        Location location,
        List<Func<Http11ExpressContext, Func<Http11ExpressContext, Task>, Task>>? middlewares,
        List<string>? keys = null)
    {
        keys ??= [HttpConstants.Get];

        // Trim out the wildcard to get the base route for static files
        builder.ServeMpaFiles(route.Replace("*", ""), location);

        builder.AddManualPipeline(
            route,
            keys,

            // Pure delegate: static + only uses parameters/local variables
            static ctx =>
            {
                var routePath = ctx.Request.Route;

                // Try the cache first
                if (!WiredApp<Http11ExpressContext>.StaticCachedResourceFiles.TryGetValue(routePath, out var resource))
                {
                    if (!Path.HasExtension(routePath))
                    {
                        // Short circuit to index.html for SPA routes without extensions

                        if (WiredApp<Http11ExpressContext>.TryReadFallbackMpaResource(ctx.Request.Route, out var indexHtml))
                        {
                            // Cache the resource for future requests and short circuit to static file endpoint
                            WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[ctx.Request.Route] = indexHtml;

                            ctx.Respond()
                                .Status(ResponseStatus.Ok)
                                .Type(MimeTypes.GetSpaMimeType(routePath))
                                .Content(CreateBoundHandler(ctx.Writer, indexHtml), (ulong)indexHtml.Length);
                        }

                        return Task.CompletedTask;
                    }

                    // Load from source on miss
                    if (!WiredApp<Http11ExpressContext>.TryReadResource(routePath, out resource))
                    {
                        // Not found
                        ctx.Respond()
                            .Status(ResponseStatus.NotFound)
                            .Type("text/plain"u8)
                            .Content("Resource was not found."u8);

                        return Task.CompletedTask;
                    }

                    // Populate cache
                    WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[routePath] = resource;
                }

                // Serve cached (or freshly loaded) resource
                var handler = CreateBoundHandler(ctx.Writer, resource);

                ctx.Respond()
                    .Status(ResponseStatus.Ok)
                    .Type(MimeTypes.GetSpaMimeType(routePath))
                    .Content(handler, (ulong)resource.Length);

                return Task.CompletedTask;
            },
            middlewares);

        return builder;
    }

    /// <summary>
    /// A static delegate used to avoid repeated allocations of identical lambda closures.
    /// Bound per-request to a specific <see cref="PipeWriter"/> and resource payload.
    /// </summary>
    private static readonly Action<PipeWriter, ReadOnlyMemory<byte>> StaticResourceHandler = Handler;

    /// <summary>
    /// Creates a pre-bound, no-closure <see cref="Action"/> that writes the given resource
    /// to the provided <see cref="PipeWriter"/> using the <see cref="StaticResourceHandler"/>.
    ///
    /// This indirection allows <see cref="Respond().Content"/> to remain fully generic
    /// while ensuring zero runtime captures or per-request delegate allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Action CreateBoundHandler(PipeWriter writer, ReadOnlyMemory<byte> resource)
        => () => StaticResourceHandler.Invoke(writer, resource);

    /// <summary>
    /// Performs the actual resource write to the response pipeline.
    /// This is an aggressively inlined, synchronous, zero-copy operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Handler(PipeWriter writer, ReadOnlyMemory<byte> resource)
        => writer.Write(resource.Span);
}

/// <summary>
/// Provides precompiled static resource request handlers for Wired.IO applications.
/// 
/// This class encapsulates the logic required to serve both **static resources** (e.g. CSS, JS, images)
/// and **SPA resources** (e.g. index.html fallbacks for client-side routing).
///
/// Each handler is built as a closed delegate pipeline compatible with the request dispatch system:
///   IServiceProvider → (Http11ExpressContext → Task)
///
/// Handlers are completely allocation-free, use cached embedded resources, and write directly
/// to the <see cref="PipeWriter"/> associated with the HTTP response stream.
/// </summary>
internal static class StaticResources
{
    /// <summary>
    /// Creates a handler for serving *regular static files* (e.g. .css, .js, .png).
    ///
    /// The returned delegate can be registered in the DI-based route table and will:
    ///   - Cast the supplied context to <see cref="Http11ExpressContext"/>
    ///   - Look up the cached resource bytes in <see cref="WiredApp{TContext}.StaticCachedResourceFiles"/>
    ///   - Resolve an appropriate MIME type from <see cref="MimeTypes.GetMimeType"/>
    ///   - Write the content synchronously to the connection’s <see cref="PipeWriter"/>
    /// </summary>
    public static Func<Http11ExpressContext, Task> CreateStaticResourceHandler()
    {
        // Return the actual static resource request handler.
        return static ctx =>
        {
            // Lookup preloaded embedded or cached static resource bytes.
            var resource = WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[ctx.Request.Route];

            // Create a bound, non-allocating handler delegate for this resource.
            var handler = CreateBoundHandler(ctx.Writer, resource);

            //Console.WriteLine($"Serving resource: {context.Request.Route} || size: {resource.Length} || MIME: {MimeTypes.GetMimeType(context.Request.Route).ToString()}");

            // Write the response with the correct MIME type and content length.
            ctx.Respond()
                .Status(ResponseStatus.Ok)
                .Type(MimeTypes.GetMimeType(ctx.Request.Route))
                .Content(handler, (ulong)resource.Length);

            // All writes are synchronous; no async IO required.
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Creates a handler for serving *SPA entry points* (e.g. index.html or History API fallback routes).
    ///
    /// Similar to <see cref="CreateStaticResourceHandler"/>, but uses <see cref="MimeTypes.GetSpaMimeType"/>
    /// to ensure that routes without extensions or fallback requests are served as HTML documents.
    ///
    /// Example:
    ///   - Request: /dashboard → serves index.html with MIME type "text/html"
    /// </summary>
    public static Func<Http11ExpressContext, Task> CreateSpaResourceHandler()
    {
        // Return the actual SPA resource handler.
        return static ctx =>
        {
            // Lookup cached resource (usually "index.html" or equivalent).
            var resource = WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[ctx.Request.Route];

            // Create a zero-allocation bound handler.
            var handler = CreateBoundHandler(ctx.Writer, resource);

            //Console.WriteLine($"Serving resource: {context.Request.Route} || size: {resource.Length} || MIME: {Encoding.UTF8.GetString(MimeTypes.GetSpaMimeType(context.Request.Route))}");

            // Serve the content with HTML MIME type fallback.
            ctx.Respond()
                .Status(ResponseStatus.Ok)
                .Type(MimeTypes.GetSpaMimeType(ctx.Request.Route))
                .Content(handler, (ulong)resource.Length);

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Creates a handler for serving *MPA entry points* (e.g. index.html or History API fallback routes).
    ///
    /// Similar to <see cref="CreateStaticResourceHandler"/>, but uses <see cref="MimeTypes.GetSpaMimeType"/>
    /// to ensure that routes without extensions or fallback requests are served as HTML documents.
    ///
    /// Example:
    ///   - Request: /dashboard → serves index.html with MIME type "text/html"
    /// </summary>
    public static Func<Http11ExpressContext, Task> CreateMpaResourceHandler()
    {
        // Return the actual MPA resource handler.
        return static ctx =>
        {
            // Lookup cached resource (usually "index.html" or equivalent).
            var resource = WiredApp<Http11ExpressContext>.StaticCachedResourceFiles[ctx.Request.Route];

            // Create a zero-allocation bound handler.
            var handler = CreateBoundHandler(ctx.Writer, resource);

            //Console.WriteLine($"Serving resource: {context.Request.Route} || size: {resource.Length} || MIME: {Encoding.UTF8.GetString(MimeTypes.GetSpaMimeType(context.Request.Route))}");

            // Serve the content with HTML MIME type fallback.
            ctx.Respond()
                .Status(ResponseStatus.Ok)
                .Type(MimeTypes.GetSpaMimeType(ctx.Request.Route))
                .Content(handler, (ulong)resource.Length);

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// A static delegate used to avoid repeated allocations of identical lambda closures.
    /// Bound per-request to a specific <see cref="PipeWriter"/> and resource payload.
    /// </summary>
    private static readonly Action<PipeWriter, ReadOnlyMemory<byte>> StaticResourceHandler = Handler;

    /// <summary>
    /// Creates a pre-bound, no-closure <see cref="Action"/> that writes the given resource
    /// to the provided <see cref="PipeWriter"/> using the <see cref="StaticResourceHandler"/>.
    ///
    /// This indirection allows <see cref="Respond().Content"/> to remain fully generic
    /// while ensuring zero runtime captures or per-request delegate allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Action CreateBoundHandler(PipeWriter writer, ReadOnlyMemory<byte> resource)
        => () => StaticResourceHandler.Invoke(writer, resource);

    /// <summary>
    /// Performs the actual resource write to the response pipeline.
    /// This is an aggressively inlined, synchronous, zero-copy operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Handler(PipeWriter writer, ReadOnlyMemory<byte> resource)
        => writer.Write(resource.Span);
}
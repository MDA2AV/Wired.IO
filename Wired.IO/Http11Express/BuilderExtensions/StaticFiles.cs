using Wired.IO.App;
using Wired.IO.Builder;
using Wired.IO.Http11Express.Context;
using Wired.IO.Http11Express.StaticHandlers;

namespace Wired.IO.Http11Express.BuilderExtensions;

/// <summary>
/// Extension methods that wire up **static** and **SPA** file serving into
/// a <see cref="Builder{THandler,TContext}"/> configured for Http/1.1 Express.
///
/// These helpers:
///  • Call your low-level resource registration (<c>ServeStaticFiles</c>/<c>ServeSpaFiles</c>)<br/>
///  • Expose an internal diagnostic endpoint to exercise the handlers directly
///    (useful for testing, smoke checks, or benchmarks)
/// </summary>
public static class StaticFiles
{
    /// <summary>
    /// Registers **static file** serving for the given <paramref name="baseRoute"/> and <paramref name="location"/>,
    /// and maps an internal GET endpoint (<c>/serve-static-resource</c>) that executes the static resource handler.
    ///
    /// Typical use:
    /// <code>
    /// builder.ServeStaticFilesExpress("/assets", Location.Embedded("MyApp.Resources.wwwroot"));
    /// </code>
    /// </summary>
    /// <param name="builder">The HTTP/1.1 Express builder.</param>
    /// <param name="baseRoute">
    /// The route prefix under which static resources are exposed (e.g. <c>"/assets"</c> or <c>"/"</c>).
    /// </param>
    /// <param name="location">
    /// The source of the files (e.g., embedded resources or filesystem folder). The concrete
    /// <c>Location</c> abstraction should encapsulate how files are discovered and cached.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    public static Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> ServeStaticFilesExpress(
        this Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> builder,
        string baseRoute,
        Location location)
    {
        // Register low-level static file plumbing (resource discovery, caching, route binding).
        builder.ServeStaticFiles(baseRoute, location);

        // Internal/test endpoint that uses the static resource handler path.
        // Useful for verifying correctness without wiring full routing tables.
        builder.MapGet("/serve-static-resource", StaticResources.CreateStaticResourceHandler<Http11ExpressContext>());

        return builder;
    }

    /// <summary>
    /// Registers **SPA file** serving (including history-API fallback) for the given
    /// <paramref name="baseRoute"/> and <paramref name="location"/>, and maps an internal
    /// GET endpoint (<c>/serve-spa-resource</c>) that executes the SPA resource handler.
    ///
    /// Typical use:
    /// <code>
    /// builder.ServeSpaFilesExpress("/", Location.Embedded("MyApp.Resources.wwwroot"));
    /// // With SPA fallback behavior (no extension → text/html).
    /// </code>
    /// </summary>
    /// <param name="builder">The HTTP/1.1 Express builder.</param>
    /// <param name="baseRoute">
    /// The route prefix hosting the SPA bundle (often <c>"/"</c>).
    /// </param>
    /// <param name="location">
    /// The SPA content source (embedded or filesystem). Should include <c>index.html</c> and asset files.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    public static Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> ServeSpaFilesExpress(
        this Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> builder,
        string baseRoute,
        Location location)
    {
        // Register low-level SPA resource plumbing (index.html fallback, asset resolution).
        builder.ServeSpaFiles(baseRoute, location);

        // Internal/test endpoint that uses the SPA handler path (HTML-first semantics).
        builder.MapGet("/serve-spa-resource", StaticResources.CreateSpaResourceHandler<Http11ExpressContext>());

        return builder;
    }

    public static Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> ServeMpaFilesExpress(
        this Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> builder,
        string baseRoute,
        Location location)
    {
        // Register low-level SPA resource plumbing (index.html fallback, asset resolution).
        builder.ServeMpaFiles(baseRoute, location);

        // Internal/test endpoint that uses the SPA handler path (HTML-first semantics).
        builder.MapGet("/serve-mpa-resource", StaticResources.CreateMpaResourceHandler<Http11ExpressContext>());

        return builder;
    }
}
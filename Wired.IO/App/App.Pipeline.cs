using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    private Func<TContext, Task>? _cachedPipeline;

    /// <summary>
    /// Builds and caches the middleware pipeline by chaining each middleware around the final endpoint delegate.
    /// This method should be called only once during application startup or first request handling.
    /// </summary>
    /// <param name="middlewares">The ordered list of middleware functions.</param>
    /// <param name="endpoint">The terminal delegate that invokes the matched endpoint.</param>
    /// <exception cref="InvalidOperationException">Thrown if the pipeline build results in a null delegate.</exception>
    public void BuildPipeline(
        IList<Func<TContext, Func<TContext, Task>, Task>> middlewares,
        Func<TContext, Task> endpoint)
    {
        Func<TContext, Task> next = endpoint;

        for (var i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            var currentNext = next;
            next = ctx => middleware(ctx, currentNext);
        }

        _cachedPipeline = next ?? throw new InvalidOperationException("Pipeline build failed");
    }

    /// <summary>
    /// Executes the previously built and cached middleware pipeline.
    /// </summary>
    /// <param name="context">The request context to pass through the middleware chain.</param>
    /// <returns>A task that completes when all middleware and the final endpoint have finished executing.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the pipeline has not yet been built.</exception>
    public Task RunCachedPipeline(TContext context)
    {
        if (_cachedPipeline is null)
            throw new InvalidOperationException("Pipeline not built");

        return _cachedPipeline(context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRouteFile(string route) => Path.HasExtension(route);


    /* Static file strategy
     *
     * If CanServeStaticFiles is enabled + route has extension + HttpMethod is GET
     *
     * Quick cache check if this full route matches a cached static file (Short circuit)
     *
     * Run through StaticResourceRouteToLocation checking all keys, in this case if the route starts with any of the keys
     * (It will always match even if it's just "/")
     *
     * Check the file system/embedded resources, if found, cache it for future requests
     *
     * If the files exists, build the response with the file content (on the context) and return the specific endpoint for static files
     * This way, it still goes through the pipeline but the endpoint is already resolved to static file handler
     *
     * In the endpoint, it should seek for the cached file content and write it to the response, if file isn't cached yet, cache it first
     *
     *
     * It is assumed that these files are static and can never change during the app lifetime
     * For dynamic files, a different "middleware" should be used that checks the file system/embedded resources on each request
     *
     */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetStaticResourceBaseRoute(string route)
    {
        ReadOnlySpan<char> input = route;

        foreach (var key in StaticResourceRouteToLocation.Keys)
            if (input.StartsWith(key.AsSpan(), StringComparison.Ordinal)) return key;

        return "/";
    }

    private bool TryReadFallbackSpaResource(string route, out ReadOnlyMemory<byte> resource)
    {
        var baseRoute = GetStaticResourceBaseRoute(route);
        var location = StaticResourceRouteToLocation[baseRoute];
        var filePath = $"{baseRoute}index.html";

        // Check if file exists in the file system
        if (location.LocationType == LocationType.FileSystem)
        {
            //var fullFilePath = $"{location.Path.TrimEnd('/', '\\')}/{baseRoute.TrimStart('/', '\\')}";
            var fullFilePath = PathUtils.Combine(location.Path, filePath);

            if (File.Exists(fullFilePath))
            {
                // Read file and return it
                resource = File.ReadAllBytes(fullFilePath);
                return true;
            }
        }

        // If location is EmbeddedResource, check if the resource exists in the assembly
        if (location is { LocationType: LocationType.EmbeddedResource, Assembly: not null })
        {
            var resourceName = location.Assembly.GetManifestResourceNames()
                .FirstOrDefault(rn => rn.EndsWith(filePath.Replace('/', '.'), StringComparison.Ordinal));

            if (resourceName is null)
            {
                resource = null!;
                return false;
            }

            // Read Embedded resource and return it
            resource = EmbeddedResourceUtils.ReadBytes(location.Assembly, resourceName);
            return true;
        }

        resource = null!;
        return false;
    }

    private bool TryReadResource(string route, out ReadOnlyMemory<byte> resource)
    {
        var baseRoute = GetStaticResourceBaseRoute(route);
        var location = StaticResourceRouteToLocation[baseRoute];
        var filePath = route[(baseRoute.Length)..];

        // Check if file exists in the file system
        if (location.LocationType == LocationType.FileSystem)
        {
            //var fullFilePath = $"{location.Path.TrimEnd('/', '\\')}/{baseRoute.TrimStart('/', '\\')}";
            var fullFilePath = PathUtils.Combine(location.Path, filePath);

            if (File.Exists(fullFilePath))
            {
                // Read file and return it
                resource = File.ReadAllBytes(fullFilePath);
                return true;
            }
        }

        // If location is EmbeddedResource, check if the resource exists in the assembly
        if (location is { LocationType: LocationType.EmbeddedResource, Assembly: not null })
        {
            var resourceName = location.Assembly.GetManifestResourceNames()
                .FirstOrDefault(rn => rn.EndsWith(filePath.Replace('/', '.'), StringComparison.Ordinal));

            if (resourceName is null)
            {
                resource = null!;
                return false;
            }

            // Read Embedded resource and return it
            resource = EmbeddedResourceUtils.ReadBytes(location.Assembly, resourceName);
            return true;
        }

        resource = null!;
        return false;
    }

    // TODO: Cache all the endpoints Dictionary<route, Func<TContext, Task>>

    /// <summary>
    /// Resolves and invokes the endpoint matching the request method and route.
    /// </summary>
    /// <param name="context">The current request context containing the route and method information.</param>
    /// <returns>A task representing the asynchronous execution of the endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no matching endpoint is found.</exception>
    public Task EndpointInvoker(TContext context)
    {
        //var httpMethod = context.Request.HttpMethod.ToUpperInvariant();
        var httpMethod = context.Request.HttpMethod;

        if (CanServeStaticFiles)
        {
            if (Path.HasExtension(context.Request.Route))
            {
                if (httpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    // Quick cache check
                    if (StaticCachedResourceFiles.ContainsKey(context.Request.Route))
                    {
                        // Resource is already cached, short circuit to static file endpoint
                        return CanServeSpaFiles ?
                            Endpoints["GET_/serve-spa-resource"].Invoke(context) : 
                            Endpoints["GET_/serve-static-resource"].Invoke(context);
                    }

                    // Resource is not cached, check if it exists
                    if (TryReadResource(context.Request.Route, out var resource))
                    {
                        // Cache the resource for future requests and short circuit to static file endpoint
                        StaticCachedResourceFiles[context.Request.Route] = resource;
                        return CanServeSpaFiles ?
                            Endpoints["GET_/serve-spa-resource"].Invoke(context) :
                            Endpoints["GET_/serve-static-resource"].Invoke(context);
                    }

                    // Else if resource does not exist, continue to normal endpoint resolution
                }
            }
        }

        var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], context.Request.Route);

        // If no matching route is found and SPA enabled, serve index.html in case the route starts with any of the SPA base routes
        if (decodedRoute is null)
        {
            if (CanServeSpaFiles)
            {
                // Serve the index.html for the given base route
                if (TryReadFallbackSpaResource(context.Request.Route, out var resource))
                {
                    // Cache the resource for future requests and short circuit to static file endpoint
                    StaticCachedResourceFiles[context.Request.Route] = resource;
                    return Endpoints["GET_/serve-spa-resource"].Invoke(context);
                }
            }
        }

        var endpoint = Endpoints[httpMethod + "_" + decodedRoute!];

        return endpoint is null
            ? throw new InvalidOperationException("Unable to find the Invoke method on the resolved service.")
            : endpoint.Invoke(context);
    }

    /// <summary>
    /// Entry point for processing an incoming request through the middleware pipeline.
    /// This method ensures a per-request scoped DI container and runs the full pipeline.
    /// </summary>
    /// <param name="context">The request context to process.</param>
    /// <returns>A task that completes when request processing is finished.</returns>
    public async Task Pipeline(TContext context)
    {
        await using var scope = Services.CreateAsyncScope();
        context.Scope = scope;

        // No caching
        // await PipelineRecursive(context, 0, Middleware);
        // await PipelineIterative(context, Middleware);

        await RunCachedPipeline(context);
    }

    /// <summary>
    /// A per-request recursive middleware pipeline executor.
    /// This approach is not cached and invokes the middleware chain using recursive function calls.
    /// </summary>
    public Task PipelineRecursive(
        TContext context,
        int index,
        IList<Func<TContext, Func<TContext, Task>, Task>> middlewares)
    {
        if (index < middlewares.Count)
        {
            return middlewares[index](context, async (ctx) => await PipelineRecursive(ctx, index + 1, middlewares));
        }

        var httpMethod = context.Request.HttpMethod.ToUpper();
        var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], context.Request.Route);
        var endpoint = Endpoints[httpMethod + "_" + decodedRoute!];

        return endpoint is null
            ? throw new InvalidOperationException("Unable to find the Invoke method on the resolved service.")
            : endpoint.Invoke(context);
    }

    /// <summary>
    /// A per-request iterative middleware executor.
    /// This builds the middleware chain on every request (no caching) using closures but avoids recursion.
    /// </summary>
    public Task PipelineIterative(
        TContext context,
        IList<Func<TContext, Func<TContext, Task>, Task>> middlewares)
    {
        Func<TContext, Task> next = ctx =>
        {
            var httpMethod = ctx.Request.HttpMethod.ToUpper();
            var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], ctx.Request.Route);
            var endpoint = Endpoints[httpMethod + "_" + decodedRoute!];

            return endpoint is null
                ? throw new InvalidOperationException("Unable to find the Invoke method on the resolved service.")
                : endpoint.Invoke(ctx);
        };

        for (var i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            var currentNext = next;
            next = ctx => middleware(ctx, currentNext);
        }

        return next(context);
    }

    /// <summary>
    /// Caches matched routes for previously seen paths to speed up route resolution.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string?> RouteMatchCache = new();

    /// <summary>
    /// Caches compiled regular expressions for each route pattern to avoid recompilation.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> RouteRegexCache = new();

    /// <summary>
    /// Matches a request route against a set of encoded patterns using cached regular expressions.
    /// If a match is found, the matching pattern is returned and cached.
    /// </summary>
    /// <param name="patterns">A set of registered route patterns for the current HTTP method.</param>
    /// <param name="input">The actual route string from the request.</param>
    /// <returns>
    /// The matching pattern if found; otherwise, <c>null</c>.
    /// </returns>
    public static string? MatchEndpoint(HashSet<string> patterns, string input)
    {
        if (RouteMatchCache.TryGetValue(input, out var cachedPattern))
            return cachedPattern;

        foreach (var pattern in patterns)
        {
            var regex = RouteRegexCache.GetOrAdd(pattern, static p =>
                new Regex(ConvertToRegex(p), RegexOptions.Compiled | RegexOptions.CultureInvariant));

            if (!regex.IsMatch(input)) continue;

            RouteMatchCache[input] = pattern;
            return pattern;
        }

        RouteMatchCache[input] = null;
        return null;
    }

    /// <summary>
    /// Converts a route pattern with placeholders (e.g., <c>/users/:id</c>) into a regular expression pattern.
    /// </summary>
    /// <param name="pattern">The route pattern containing optional placeholders (e.g., <c>:id</c>).</param>
    /// <returns>A regex string that matches the route with placeholders replaced by wildcards.</returns>
    public static string ConvertToRegex(string pattern)
    {
        var regexPattern = Regex.Replace(pattern, @":\w+", "[^/]+");
        return $"^{regexPattern}$";
    }
}
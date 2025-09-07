using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

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

    /// <summary>
    /// Resolves and invokes the endpoint matching the request method and route.
    /// </summary>
    /// <param name="context">The current request context containing the route and method information.</param>
    /// <returns>A task representing the asynchronous execution of the endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no matching endpoint is found.</exception>
    public Task EndpointInvoker(TContext context)
    {
        var httpMethod = context.Request.HttpMethod.ToUpperInvariant();
        var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], context.Request.Route);
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
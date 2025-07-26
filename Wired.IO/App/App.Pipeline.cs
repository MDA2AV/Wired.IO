using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Wired.IO.App;

public partial class WiredApp<TContext>
{
    /// <summary>
    /// Executes the middleware pipeline recursively for a given request context.
    /// Once all middleware are processed, invokes the matched endpoint.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <param name="index">The index of the current middleware being executed.</param>
    /// <param name="middlewares">The list of middleware functions to invoke.</param>
    /// <returns>A task that completes when the pipeline has finished executing.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no endpoint is found for the resolved route.
    /// </exception>
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

    public Task PipelineIterative(
        TContext context,
        IList<Func<TContext, Func<TContext, Task>, Task>> middlewares)
    {
        // Final delegate (the actual endpoint handler)
        Func<TContext, Task> next = ctx =>
        {
            var httpMethod = ctx.Request.HttpMethod.ToUpper();
            var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], ctx.Request.Route);
            var endpoint = Endpoints[httpMethod + "_" + decodedRoute!];

            return endpoint is null
                ? throw new InvalidOperationException("Unable to find the Invoke method on the resolved service.")
                : endpoint.Invoke(ctx);
        };

        // Build the pipeline in reverse order
        for (int i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            var currentNext = next; // Capture current state of the pipeline

            next = ctx => middleware(ctx, currentNext);
        }

        // Execute the complete pipeline
        return next(context);
    }

    /// <summary>
    /// Entry point for processing an incoming request through the middleware pipeline.
    /// Creates a scoped service provider for the request and invokes <see cref="PipelineNoResponse(TContext)"/>.
    /// </summary>
    /// <param name="context">The request context to process.</param>
    /// <returns>A task that completes when request processing is finished.</returns>
    public async Task PipelineNoResponse(TContext context)
    {
        await using var scope = Services.CreateAsyncScope();
        context.Scope = scope;

        //await PipelineRecursive(context, 0, Middleware);

        //await PipelineIterative(context, Middleware);

        if (!_pipelineBuilt)
        {
            _pipelineBuilt = true;
            BuildPipeline(Middleware, EndpointInvoker);
        }

        await RunCachedPipeline(context);
    }

    private Func<TContext, Task>? _cachedPipeline;

    private bool _pipelineBuilt;

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

    public Task RunCachedPipeline(TContext context)
    {
        if (_cachedPipeline is null)
            throw new InvalidOperationException("Pipeline not built");

        return _cachedPipeline(context);
    }

    public Task EndpointInvoker(TContext context)
    {
        var httpMethod = context.Request.HttpMethod.ToUpperInvariant();
        var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], context.Request.Route);
        var endpoint = Endpoints[httpMethod + "_" + decodedRoute!];

        return endpoint is null
            ? throw new InvalidOperationException("Unable to find the Invoke method on the resolved service.")
            : endpoint.Invoke(context);
    }

    private static readonly ConcurrentDictionary<string, string?> RouteMatchCache = new();
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
        // Check if we've seen this exact input before
        if (RouteMatchCache.TryGetValue(input, out var cachedPattern))
            return cachedPattern;

        foreach (var pattern in patterns)
        {
            var regex = RouteRegexCache.GetOrAdd(pattern, static p =>
                new Regex(ConvertToRegex(p), RegexOptions.Compiled | RegexOptions.CultureInvariant));

            if (!regex.IsMatch(input)) 
                continue;

            // Cache for next time
            RouteMatchCache[input] = pattern;
            return pattern;
        }

        // No match found — cache null
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
        // Replace placeholders like ":id" with a regex pattern that matches any non-slash characters
        var regexPattern = Regex.Replace(pattern, @":\w+", "[^/]+");

        // Add anchors to ensure the regex matches the entire input string
        regexPattern = $"^{regexPattern}$";

        return regexPattern;
    }
}
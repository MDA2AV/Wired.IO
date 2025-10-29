using Microsoft.Extensions.DependencyInjection;
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
    private Task RunCachedPipeline(TContext context)
    {
        if (_cachedPipeline is null)
            throw new InvalidOperationException("Pipeline not built");

        return _cachedPipeline(context);
    }

    // TODO: Cache all the endpoints Dictionary<route, Func<TContext, Task>>

    private readonly Dictionary<string, Func<TContext, Task>> _cachedEndpoints = new();

    /// <summary>
    /// Resolves and invokes the endpoint matching the request method and route.
    /// </summary>
    /// <param name="context">The current request context containing the route and method information.</param>
    /// <returns>A task representing the asynchronous execution of the endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no matching endpoint is found.</exception>
    public Task EndpointInvoker(TContext context)
    {
        if(_cachedEndpoints.TryGetValue(context.Request.Route, out var cachedEndpoint))
            return cachedEndpoint.Invoke(context);
        
        var httpMethod = context.Request.HttpMethod;

        var decodedRoute = MatchEndpoint(RootEncodedRoutes[httpMethod], context.Request.Route);

        if (decodedRoute is null)
        {
            return RootEndpoints["FlowControl_NotFound"].Invoke(context);
        }

        var endpoint = RootEndpoints[httpMethod + "_" + decodedRoute!];
        _cachedEndpoints[context.Request.Route] = endpoint;

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
    internal async Task RootPipeline(TContext context)
    {
        if (ScopedEndpoints)
        {
            await using var scope = Services.CreateAsyncScope();
            context.Services = scope.ServiceProvider;
            await RunCachedPipeline(context);

            return;
        }

        context.Services = Services;
        await RunCachedPipeline(context);

        // No caching
        // await PipelineRecursive(context, 0, Middleware);
        // await PipelineIterative(context, Middleware);
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
        var endpoint = RootEndpoints[httpMethod + "_" + decodedRoute!];

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
            var endpoint = RootEndpoints[httpMethod + "_" + decodedRoute!];

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
}
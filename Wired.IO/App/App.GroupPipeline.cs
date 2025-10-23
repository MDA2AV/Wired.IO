using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Builder;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    private Dictionary<EndpointKey, ImmutableArray<string>> _endpointMiddlewareMap =
        new Dictionary<EndpointKey, ImmutableArray<string>>();

    public void SetCompiledRoutes(CompiledRoutes compiled)
    {
        // Build a dictionary for O(1) lookup at request time
        var dict = new Dictionary<EndpointKey, ImmutableArray<string>>(compiled.Endpoints.Length);
        foreach (var ep in compiled.Endpoints)
            dict[ep.Key] = ep.MiddlewarePrefixes;

        _endpointMiddlewareMap = dict;
    }

    private readonly ConcurrentDictionary<EndpointKey, Func<TContext, Task>> _pipelineCache = new();

    private static Func<TContext, Task> ComposePipeline(
        IReadOnlyList<Func<TContext, Func<TContext, Task>, Task>> middlewares,
        Func<TContext, Task> terminal)
    {
        var next = terminal;
        for (int i = middlewares.Count - 1; i >= 0; i--)
        {
            var mw = middlewares[i];
            var captured = next;
            next = ctx => mw(ctx, captured);
        }
        return next;
    }

    private Func<TContext, Task> BuildPipelineFor(
        EndpointKey key,
        ImmutableArray<string> middlewarePrefixes,
        IServiceProvider sp)
    {
        // 1) resolve the terminal endpoint by EndpointKey
        var endpoint = sp.GetRequiredKeyedService<Func<TContext, Task>>(key);

        // 2) resolve all middlewares for each prefix (in order), append to a flat list
        var list = new List<Func<TContext, Func<TContext, Task>, Task>>(8);
        foreach (var prefix in middlewarePrefixes)
        {
            var groupMws = sp.GetKeyedServices<Func<TContext, Func<TContext, Task>, Task>>(prefix);
            if (groupMws is null) continue;
            // registration order is preserved by DI
            foreach (var mw in groupMws)
                list.Add(mw);
        }

        // 3) compose and return
        return ComposePipeline(list, endpoint);
    }

    private Func<TContext, Task> ResolveOrBuildCachedPipeline(
        EndpointKey key,
        IServiceProvider sp)
    {
        if (_pipelineCache.TryGetValue(key, out var cached))
            return cached;

        if (!_endpointMiddlewareMap.TryGetValue(key, out var prefixes))
            throw new InvalidOperationException($"No compiled metadata for endpoint {key.Method} {key.Path}");

        var built = BuildPipelineFor(key, prefixes, sp);
        _pipelineCache[key] = built;
        return built;
    }

    public Task EndpointInvoker2(TContext context)
    {
        // ... your existing static-file short-circuits remain unchanged ...

        var httpMethod = context.Request.HttpMethod;
        var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], context.Request.Route);

        if (decodedRoute is null)
        {
            // your SPA/MPA fallbacks remain unchanged; otherwise:
            return Endpoints["FlowControl_NotFound"].Invoke(context);
        }

        // Build the same "full path" used in mapping (you already have it as decodedRoute)
        var key = new EndpointKey(httpMethod, decodedRoute);

        // If you run ScopedEndpoints, you already create a scope in Pipeline(); do it here to fetch from DI:
        if (ScopedEndpoints)
        {
            return InvokeScoped(context, key);
        }
        else
        {
            // No scope: use root provider
            var pipeline = ResolveOrBuildCachedPipeline(key, Services);
            return pipeline(context);
        }
    }

    private async Task InvokeScoped(TContext context, EndpointKey key)
    {
        await using var scope = Services.CreateAsyncScope();
        context.Services = scope.ServiceProvider;

        var pipeline = ResolveOrBuildCachedPipeline(key, scope.ServiceProvider);
        await pipeline(context);
    }

    private Task GroupPipeline(TContext context)
    {
        var key = new EndpointKey(
            context.Request.HttpMethod,
            MatchEndpoint(EncodedRoutes[context.Request.HttpMethod], context.Request.Route) ?? string.Empty);

        if (ScopedEndpoints)
            return InvokeScoped(context, key);

        // root scope
        context.Services = Services;
        var pipeline = ResolveOrBuildCachedPipeline(key, Services);
        return pipeline(context);
    }
}
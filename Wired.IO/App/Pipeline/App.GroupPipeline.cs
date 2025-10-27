using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Wired.IO.Builder;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

// Diogo here, with group endpoints all static resource serving can be in a middleware!

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    private Dictionary<EndpointKey, ImmutableArray<string>> _endpointMiddlewareMap =
        new Dictionary<EndpointKey, ImmutableArray<string>>();
    
    // Rearranged data from CompiledRoutes.CompiledEndpoints
    internal readonly ConcurrentDictionary<EndpointKey, Func<TContext, Task>> _pipelineCache = new();

    internal void SetCompiledRoutes(CompiledRoutes compiledRoutes)
    {
        _endpointMiddlewareMap = new Dictionary<EndpointKey, ImmutableArray<string>>(compiledRoutes.CompiledEndpoints.Length);
        foreach (var compiledEndpoint in compiledRoutes.CompiledEndpoints)
            _endpointMiddlewareMap[compiledEndpoint.Key] = compiledEndpoint.MiddlewarePrefixes;
    }

    private static Func<TContext, Task> ComposePipeline(
        List<Func<TContext, Func<TContext, Task>, Task>> middlewares,
        Func<TContext, Task> endpoint)
    {
        var next = endpoint;
        for (var i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            var captured = next;
            
            // TODO: Try with async and without, any perf changes?
            next = async ctx => await middleware(ctx, captured);
        }
        return next;
    }

    private static Func<TContext, Task> BuildPipelineFor(
        EndpointKey key,
        ImmutableArray<string> middlewarePrefixes,
        IServiceProvider sp)
    {
        var middlewares = new List<Func<TContext, Func<TContext, Task>, Task>>(16); // More than 16 middlewares is insanity
        middlewares.AddRange(middlewarePrefixes.SelectMany(prefix => 
            sp.GetKeyedServices<Func<TContext, Func<TContext, Task>, Task>>(prefix)));
        
        return ComposePipeline(middlewares, sp.GetRequiredKeyedService<Func<TContext, Task>>(key));
    }

    internal Func<TContext, Task> BuildManualPipelineFor(
        EndpointKey key,
        List<Func<TContext, Func<TContext, Task>, Task>>? middlewares,
        IServiceProvider sp)
    {
        if (middlewares is null) 
            return sp.GetRequiredKeyedService<Func<TContext, Task>>(key);

        var endpoint = sp.GetRequiredKeyedService<Func<TContext, Task>>(key);

        return ComposePipeline(middlewares, endpoint);
    }

    private Func<TContext, Task> ResolveOrBuildCachedPipeline(EndpointKey key)
    {
        if (_pipelineCache.TryGetValue(key, out var cached))
            return cached;

        return RootEndpoints["FlowControl_NotFound"];
    }

    internal List<ManualPipelineEntry> ManualPipelineEntries { get; } = new();

    internal void CachePipelines(IServiceProvider sp)
    {
        foreach (var manualPipelineEntry in ManualPipelineEntries)
        {
            _pipelineCache[manualPipelineEntry.EndpointKey] = 
                BuildManualPipelineFor(manualPipelineEntry.EndpointKey, manualPipelineEntry.Middlewares, sp);
        }
        
        foreach (var kvp in _endpointMiddlewareMap)
        {
            _pipelineCache[kvp.Key] = BuildPipelineFor(kvp.Key, kvp.Value, sp);
        }
    }

    internal async Task GroupPipeline(TContext context)
    {
        var matchedRoute = MatchEndpoint(EncodedRoutes[context.Request.HttpMethod], context.Request.Route);

        var endpointKey = new EndpointKey();

        if (matchedRoute is not null)
        {
            endpointKey = new EndpointKey(context.Request.HttpMethod, matchedRoute);
        }
        else
        {
            var match = PartialExactMatchRoutes
                .SelectMany(
                    kvp => kvp.Value
                        .Where(prefix => context.Request.Route.AsSpan().StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
                        .Select(prefix => (Key: kvp.Key, Prefix: prefix))
                )
                .FirstOrDefault();

            if (match.Key is not null && match.Prefix is not null)
            {
                endpointKey = new EndpointKey(match.Key, match.Prefix);
            }
        }
        
        if (ScopedEndpoints)
            await InvokeScoped(context, endpointKey);
        
        await InvokeNonScoped(context, endpointKey);
    }
    
    private async Task InvokeScoped(TContext context, EndpointKey key)
    {
        await using var scope = Services.CreateAsyncScope();
        context.Services = scope.ServiceProvider;
        var pipeline = ResolveOrBuildCachedPipeline(key);
        
        await pipeline(context);
    }

    private async Task InvokeNonScoped(TContext context, EndpointKey key)
    {
        context.Services = Services;
        var pipeline = ResolveOrBuildCachedPipeline(key);
        
        await pipeline(context);
    }
    internal class ManualPipelineEntry
    {
        internal EndpointKey EndpointKey { get; set; }
        internal List<Func<TContext, Func<TContext, Task>, Task>>? Middlewares { get; set; }
    }
}
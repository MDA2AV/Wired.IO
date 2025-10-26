using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ConcurrentDictionary<EndpointKey, Func<TContext, Task>> _pipelineCache = new();

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

    private Func<TContext, Task> BuildPipelineFor(
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
        
        return ComposePipeline(middlewares, sp.GetRequiredKeyedService<Func<TContext, Task>>(key));
    }

    private Func<TContext, Task> ResolveOrBuildCachedPipeline(
        EndpointKey key,
        IServiceProvider sp)
    {
        if (_pipelineCache.TryGetValue(key, out var cached))
            return cached;
        
        return _pipelineCache[_notFoundEndpointKey];
    }

    private readonly EndpointKey _notFoundEndpointKey =  new EndpointKey("FlowControl", "NotFound");

    internal List<Func<TContext, Task>> ManuallyBuiltPipelines { get; set; } = new();

    internal void CachePipelines(IServiceProvider sp)
    {
        // Build a manual pipeline here
        _pipelineCache[_notFoundEndpointKey] = RootEndpoints["FlowControl_NotFound"];
        _pipelineCache[_notFoundEndpointKey] = BuildManualPipelineFor();
        
        foreach (var kvp in _endpointMiddlewareMap)
        {
            _pipelineCache[kvp.Key] = BuildPipelineFor(kvp.Key, kvp.Value, sp);
        }
    }

    private Func<TContext, Task> EndpointResolver(IServiceProvider sp, EndpointKey key)
    {
        if (key.Path is null)
        {
            // Short circuit to FlowControl_NotFound endpoint
            return _pipelineCache[_notFoundEndpointKey];
        }
        
        // Resolve the endpoint from the IServiceProvider and return it
        return sp.GetRequiredKeyedService<Func<TContext, Task>>(key);
    }

    internal async Task GroupPipeline(TContext context)
    {
        var matchedRoute = MatchEndpoint(EncodedRoutes[context.Request.HttpMethod], context.Request.Route);

        if (matchedRoute is null)
        {
            // This means that we couldn't find a matching route, still, look for partial matching routes
            // for cases when user wants to match /partialRoute/*
            
            // Diogo here, how to connect here, the key must match the key set by the AddManualPipeline
        }
        
        var key = new EndpointKey(context.Request.HttpMethod, matchedRoute);
        
        if (ScopedEndpoints)
            await InvokeScoped(context, key);
        
        await InvokeNonScoped(context, key);
    }
    
    private async Task InvokeScoped(TContext context, EndpointKey key)
    {
        await using var scope = Services.CreateAsyncScope();
        context.Services = scope.ServiceProvider;
        var pipeline = ResolveOrBuildCachedPipeline(key, scope.ServiceProvider);
        
        await pipeline(context);
    }

    private async Task InvokeNonScoped(TContext context, EndpointKey key)
    {
        context.Services = Services;
        var pipeline = ResolveOrBuildCachedPipeline(key, Services);
        
        await pipeline(context);
    }
}
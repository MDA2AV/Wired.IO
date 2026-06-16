using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Wired.IO.App;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Builder;

// TODO: Refactor this file, better naming and potentially less classes/inner classes

public sealed partial class Builder<THandler, TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    where THandler : IHttpHandler
{
    // Public entry that creates/returns a top-level group
    public Group MapGroup(string groupRoute)
        => _root.GetOrAddChild(RouteUtils.Normalize(groupRoute));

    private Group _root = null!;
    private EndpointDIRegister _endpointDIRegister = null!;

    /// <summary>
    /// Flattens the tree to (EndpointKey, MiddlewarePrefixes[]) descriptors only.
    /// Does NOT resolve from DI or compose pipelines.
    /// </summary>
    private CompiledRoutes Compile() => RouteCompiler.Compile(_root);

    // ---------------------------------------------------------------------------------

    public sealed class Group
    {
        private readonly Dictionary<string, Group> _children = new(StringComparer.Ordinal);
        private readonly List<EndpointDef> _endpoints = new();

        // Track if this group actually registered any middleware in DI.
        private int _middlewareRegistrations;

        internal Group(string prefix, Group? parent, EndpointDIRegister diRegister)
        {
            Prefix = RouteUtils.Normalize(prefix);
            Parent = parent;
            DiRegister = diRegister;
        }

        internal string Prefix { get; }
        internal Group? Parent { get; }
        internal EndpointDIRegister DiRegister { get; }

        public Group MapGroup(string groupRoute) => GetOrAddChild(groupRoute);

        public Group UseMiddleware(Func<TContext, Func<TContext, Task>, Task> middleware)
        {
            // Register middleware in DI keyed by this group's prefix (string)
            DiRegister.AddGroupMiddleware(Prefix, middleware);
            _middlewareRegistrations++; // mark that this prefix has middlewares
            return this;
        }

        public Group MapGet(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Get, route, endpoint);
        public Group MapGet(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Get, route, endpoint);

        public Group MapPost(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Post, route, endpoint);
        public Group MapPost(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Post, route, endpoint);
        
        public Group MapPut(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Put, route, endpoint);
        public Group MapPut(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Put, route, endpoint);
        
        public Group MapDelete(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Delete, route, endpoint);
        public Group MapDelete(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Delete, route, endpoint);
        
        public Group MapPatch(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Patch, route, endpoint);
        public Group MapPatch(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Patch, route, endpoint);
        
        public Group MapOptions(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Options, route, endpoint);
        public Group MapOptions(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Options, route, endpoint);
        
        public Group MapHead(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Head, route, endpoint);
        public Group MapHead(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Head, route, endpoint);
        
        public Group MapTrace(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Trace, route, endpoint);
        public Group MapTrace(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Trace, route, endpoint);
        
        public Group MapConnect(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Connect, route, endpoint);
        public Group MapConnect(string route, Action<TContext> endpoint)
            => Map(HttpConstants.Connect, route, endpoint);

        private Group Map(string method, string route, Func<TContext, Task> endpoint)
        {
            var fullPath = RouteUtils.Combine(Prefix, route);
            var key = new EndpointKey(method, fullPath);

            // Register endpoint in DI keyed by EndpointKey
            DiRegister.AddEndpoint(key, endpoint);

            _endpoints.Add(new EndpointDef(key));
            return this;
        }
        private Group Map(string method, string route, Action<TContext> endpoint)
        {
            var fullPath = RouteUtils.Combine(Prefix, route);
            var key = new EndpointKey(method, fullPath);

            // Register endpoint in DI keyed by EndpointKey
            DiRegister.AddEndpoint(key, endpoint);

            _endpoints.Add(new EndpointDef(key));
            return this;
        }

        internal Group GetOrAddChild(string groupRoute)
        {
            var childPrefix = RouteUtils.Combine(Prefix, groupRoute);
            if (_children.TryGetValue(childPrefix, out var existing))
                return existing;

            var child = new Group(childPrefix, this, DiRegister);
            _children.Add(childPrefix, child);
            return child;
        }

        internal bool HasMiddlewares => _middlewareRegistrations > 0;

        internal IEnumerable<Group> Children => _children.Values;
        internal IReadOnlyList<EndpointDef> Endpoints => _endpoints;
    }

    // ---------------------------------------------------------------------------------

    internal sealed class EndpointDIRegister
    {
        // Keeping reference to the builder, this is required because
        // the IServiceCollection might change due to embedding
        private readonly Builder<THandler, TContext> _builder;
        //private readonly IServiceCollection _services;

        public EndpointDIRegister(Builder<THandler, TContext> builder) => _builder = builder;

        // Endpoints keyed by EndpointKey
        public void AddEndpoint(EndpointKey key, Func<TContext, Task> endpoint)
        {
            _builder.Services.AddKeyedScoped<Func<TContext, Task>>(key, (_, _) => endpoint);
        }

        public void AddEndpoint(EndpointKey key, Action<TContext> endpoint)
        {
            _builder.Services.AddKeyedScoped<Func<TContext, Task>>(key, (_, _) => (ctx) =>
            {
                endpoint(ctx);
                return Task.CompletedTask;
            });
        }

        // Middlewares keyed by group's prefix (string)
        public void AddGroupMiddleware(string groupPrefix, Func<TContext, Func<TContext, Task>, Task> middleware)
            => _builder.Services.AddKeyedScoped<Func<TContext, Func<TContext, Task>, Task>>(groupPrefix, (_, _) => middleware);
    }

    internal readonly record struct EndpointDef(EndpointKey Key);

    // ---------------------------------------------------------------------------------

    internal static class RouteCompiler
    {
        public static CompiledRoutes Compile(Group root)
        {
            var compiledEndpoints = new List<CompiledEndpoint>(256);
            var inheritedPrefixes = new List<string>(16);

            void Dfs(Group g)
            {
                // If this group has any registered middlewares, add its prefix once.
                var added = false;
                if (g.HasMiddlewares)
                {
                    inheritedPrefixes.Add(g.Prefix);
                    added = true;
                }

                foreach (var ep in g.Endpoints)
                {
                    compiledEndpoints.Add(new CompiledEndpoint(
                        ep.Key,
                        ImmutableArray.CreateRange(inheritedPrefixes)));
                }

                foreach (var child in g.Children)
                    Dfs(child);

                if (added)
                    inheritedPrefixes.RemoveAt(inheritedPrefixes.Count - 1);
            }

            Dfs(root);

            return new CompiledRoutes
            {
                CompiledEndpoints = compiledEndpoints.ToImmutableArray()
            };
        }
    }
}


/// <summary>
/// Descriptor only: endpoint key + ordered list of middleware group prefixes
/// (ancestor-first). No delegates are resolved or composed here.
/// </summary>
public readonly record struct CompiledEndpoint(
    EndpointKey Key,
    ImmutableArray<string> MiddlewarePrefixes);

public sealed class CompiledRoutes
{
    public required ImmutableArray<CompiledEndpoint> CompiledEndpoints { get; init; }

    /// <summary>
    /// Populates (or augments) the target EncodedRoutes map with all compiled endpoints,
    /// grouped by HTTP method. Ensures key existence and avoids duplicates.
    /// </summary>
    public void PopulateEncodedRoutes(IDictionary<string, List<string>> encodedRoutes)
    {
        foreach (var compiledEndpoint in CompiledEndpoints)
        {
            var method = NormalizeMethod(compiledEndpoint.Key.Method);
            var path = compiledEndpoint.Key.Path;

            if (!encodedRoutes.TryGetValue(method, out var set))
            {
                set = [];
                encodedRoutes[method] = set;
            }

            set.Add(path!);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormalizeMethod(string method)
        => string.IsNullOrEmpty(method) ? method : method.ToUpperInvariant();
}

// Method: GET, POST, etc.
// Path: /user/:id 
public readonly record struct EndpointKey(string Method, string? Path);
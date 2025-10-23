using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Wired.IO.App;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Builder;

public sealed partial class Builder<THandler, TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    where THandler : IHttpHandler<TContext>
{
    // Public entry that creates/returns a top-level group
    public Group MapGroup(string groupRoute)
        => _root.GetOrAddChild(RouteUtils.Normalize(groupRoute));

    private Group _root = null!;
    private EndpointRegistrar _registrar = null!;

    /// <summary>
    /// Flattens the tree to (EndpointKey, MiddlewarePrefixes[]) descriptors only.
    /// Does NOT resolve from DI or compose pipelines.
    /// </summary>
    public CompiledRoutes Compile() => RouteCompiler.Compile(_root);

    // ---------------------------------------------------------------------------------

    public sealed class Group
    {
        private readonly Dictionary<string, Group> _children = new(StringComparer.Ordinal);
        private readonly List<EndpointDef<TContext>> _endpoints = new();

        // Track if this group actually registered any middleware in DI.
        private int _middlewareRegistrations;

        internal Group(string prefix, Group? parent, EndpointRegistrar registrar)
        {
            Prefix = RouteUtils.Normalize(prefix);
            Parent = parent;
            Registrar = registrar;
        }

        internal string Prefix { get; }
        internal Group? Parent { get; }
        internal EndpointRegistrar Registrar { get; }

        public Group MapGroup(string groupRoute) => GetOrAddChild(groupRoute);

        public Group UseMiddleware(Func<TContext, Func<TContext, Task>, Task> middleware)
        {
            // Register middleware in DI keyed by this group's prefix (string)
            Registrar.AddGroupMiddleware(Prefix, middleware);
            _middlewareRegistrations++; // mark that this prefix has middlewares
            return this;
        }

        public Group MapGet(string route, Func<TContext, Task> endpoint)
            => Map(HttpConstants.Get, route, endpoint);

        // Add MapPost/Put/Delete similarly…

        private Group Map(string method, string route, Func<TContext, Task> endpoint)
        {
            var fullPath = RouteUtils.Combine(Prefix, route);
            var key = new EndpointKey(method, fullPath);

            // Register endpoint in DI keyed by EndpointKey
            Registrar.AddEndpoint(key, endpoint);

            _endpoints.Add(new EndpointDef<TContext>(key));
            return this;
        }

        internal Group GetOrAddChild(string groupRoute)
        {
            var childPrefix = RouteUtils.Combine(Prefix, groupRoute);
            if (_children.TryGetValue(childPrefix, out var existing))
                return existing;

            var child = new Group(childPrefix, this, Registrar);
            _children.Add(childPrefix, child);
            return child;
        }

        internal bool HasMiddlewares => _middlewareRegistrations > 0;

        internal IEnumerable<Group> Children => _children.Values;
        internal IReadOnlyList<EndpointDef<TContext>> Endpoints => _endpoints;
    }

    // ---------------------------------------------------------------------------------

    internal static class RouteUtils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Normalize(string route)
        {
            if (string.IsNullOrWhiteSpace(route)) return "/";
            var r = route.Trim();
            if (r[0] != '/') r = "/" + r;
            while (r.Contains("//", StringComparison.Ordinal))
                r = r.Replace("//", "/", StringComparison.Ordinal);
            if (r.Length > 1 && r.EndsWith("/", StringComparison.Ordinal))
                r = r.TrimEnd('/');
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Combine(string left, string right)
        {
            if (string.IsNullOrEmpty(right)) return Normalize(left);
            if (string.IsNullOrEmpty(left)) return Normalize(right);

            var l = Normalize(left);
            var r = Normalize(right);
            if (l == "/") return r;
            if (r == "/") return l;
            return l + r;
        }
    }

    // ---------------------------------------------------------------------------------

    internal sealed class EndpointRegistrar
    {
        private readonly IServiceCollection _services;

        public EndpointRegistrar(IServiceCollection services) => _services = services;

        // Endpoints keyed by EndpointKey
        public void AddEndpoint(EndpointKey key, Func<TContext, Task> endpoint)
            => _services.AddKeyedScoped<Func<TContext, Task>>(key, (_, _) => endpoint);

        // Middlewares keyed by group's prefix (string)
        public void AddGroupMiddleware(string groupPrefix, Func<TContext, Func<TContext, Task>, Task> middleware)
            => _services.AddKeyedScoped<Func<TContext, Func<TContext, Task>, Task>>(groupPrefix, (_, _) => middleware);
    }

    internal readonly record struct EndpointDef<TCtx>(EndpointKey Key);

    // ---------------------------------------------------------------------------------

    internal static class RouteCompiler
    {
        public static CompiledRoutes Compile(Group root)
        {
            var endpoints = new List<CompiledEndpoint>(256);
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
                    endpoints.Add(new CompiledEndpoint(
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
                Endpoints = endpoints.ToImmutableArray()
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
    public required ImmutableArray<CompiledEndpoint> Endpoints { get; init; }

    /// <summary>
    /// Populates (or augments) the target EncodedRoutes map with all compiled endpoints,
    /// grouped by HTTP method. Ensures key existence and avoids duplicates.
    /// </summary>
    public void PopulateEncodedRoutes(IDictionary<string, HashSet<string>> dest)
    {
        foreach (var ep in Endpoints)
        {
            var method = NormalizeMethod(ep.Key.Method);
            var path = ep.Key.Path;

            if (!dest.TryGetValue(method, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                dest[method] = set;
            }

            set.Add(path); // HashSet => idempotent
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormalizeMethod(string method)
        => string.IsNullOrEmpty(method) ? method : method.ToUpperInvariant();
}

public readonly record struct EndpointKey(string Method, string Path);
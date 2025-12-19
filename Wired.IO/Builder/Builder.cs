using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Reflection;
using Wired.IO.App;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Response;
using IBaseRequest = Wired.IO.Protocol.Request.IBaseRequest;

namespace Wired.IO.Builder;

/// <summary>
/// Provides a fluent configuration API to build and configure a <see cref="WiredApp{TContext}"/> instance.
/// Supports setting up dependency injection, middleware, routes, TLS options, and runtime parameters.
/// </summary>
/// <typeparam name="THandler">The HTTP handler type implementing <see cref="IHttpHandler{TContext}"/>.</typeparam>
/// <typeparam name="TContext"></typeparam>
public sealed partial class Builder<THandler, TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    where THandler : IHttpHandler<TContext>
{
    /// <summary>
    /// Gets the service collection used to register middleware, handlers, and services.
    /// </summary>
    public IServiceCollection Services => App.ServiceCollection;

    /// <summary>
    /// Gets the application instance being configured.
    /// </summary>
    public WiredApp<TContext> App { get; } = new WiredApp<TContext>();

    /// <summary>
    /// Initializes the builder using a handler factory and defaults to HTTP/1.1 protocol.
    /// </summary>
    /// <param name="handlerFactory">A delegate that creates the HTTP handler.</param>
    public Builder(Func<THandler> handlerFactory)
    {
        Initialize(handlerFactory, [SslApplicationProtocol.Http11]);
    }

    /// <summary>
    /// Initializes the builder using a handler factory and custom ALPN protocols.
    /// </summary>
    /// <param name="handlerFactory">A delegate that creates the HTTP handler.</param>
    /// <param name="sslApplicationProtocols">The list of supported ALPN protocols.</param>
    public Builder(Func<THandler> handlerFactory, List<SslApplicationProtocol> sslApplicationProtocols)
    {
        Initialize(handlerFactory, sslApplicationProtocols);
    }

    /// <summary>
    /// Initializes the internal <see cref="WiredApp{TContext}"/> instance with the specified handler and ALPN protocols.
    /// </summary>
    /// <param name="handlerFactory">The handler creation delegate.</param>
    /// <param name="sslApplicationProtocols">List of supported ALPN protocols.</param>
    private void Initialize(Func<THandler> handlerFactory, List<SslApplicationProtocol> sslApplicationProtocols)
    {
        _endpointDIRegister = new EndpointDIRegister(App.ServiceCollection);
        _root = new Group(prefix: "/", parent: null, diRegister: _endpointDIRegister);

        App.HttpHandler = handlerFactory();
        App.SslServerAuthenticationOptions.ApplicationProtocols = sslApplicationProtocols;
    }

    public WiredApp<TContext> Build(IServiceProvider? serviceProvider = null!)
    {
        var isLoggerFactoryRegistered = App.ServiceCollection.Any(
            d => d.ServiceType == typeof(ILoggerFactory));

        if(!isLoggerFactoryRegistered)
            App.ServiceCollection.AddLogging(DefaultLoggingBuilder);
        
        if(App.UseRootOnlyEndpoints)
        {
            BuildRootOnly(serviceProvider);
        }
        else
        {
            BuildGroup(serviceProvider);
        }
        
        App.LoggerFactory = App.Services.GetRequiredService<ILoggerFactory>();
        App.Logger = App.LoggerFactory.CreateLogger<WiredApp<TContext>>();

        // Set up static resource routes in order of descending route length to avoid false matches
        WiredApp<TContext>.StaticResourceRouteToLocation = WiredApp<TContext>.StaticResourceRouteToLocation
            .OrderByDescending(kvp => kvp.Key.Length)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return App;
    }

    /// <summary>
    /// Supports only root endpoints and middleware, no route groups.
    /// Still supported for backwards compatibility and very simple webservers.
    /// Will eventually be deprecated.
    /// </summary>
    /// <param name="serviceProvider"></param>
    private void BuildRootOnly(IServiceProvider? serviceProvider = null!)
    {
        App.Services = serviceProvider ?? 
                       App.ServiceCollection.BuildServiceProvider();

        App.RootMiddleware = App.Services.GetServices<Func<TContext, Func<TContext, Task>, Task>>().ToList();
        App.BuildPipeline(App.RootMiddleware, App.EndpointInvoker);

        App.SetPipeline(App.RootPipeline);

        App.RootEndpoints = [];

        foreach (var fullRoute in App.RootEncodedRoutes.SelectMany(kvp => kvp.Value.Select(route => kvp.Key + '_' + route)))
        {
            App.RootEndpoints.Add(
                fullRoute,
                App.Services.GetRequiredKeyedService<Func<TContext, Task>>(fullRoute));
        }

        RearrangeEncodedRoutes(App.RootEncodedRoutes);
    }

    private void BuildGroup(IServiceProvider? serviceProvider = null!)
    {
        var compiledRoutes = Compile();

        compiledRoutes.PopulateEncodedRoutes(App.EncodedRoutes);
        App.SetCompiledRoutes(compiledRoutes);
        
        App.Services = serviceProvider ??
                       App.ServiceCollection.BuildServiceProvider();
        
        App.RootEndpoints = [];
        App.GroupEndpoints = [];

        App.RootMiddleware = App.Services.GetServices<Func<TContext, Func<TContext, Task>, Task>>().ToList();

        foreach (var fullRoute in App.RootEncodedRoutes.SelectMany(kvp => kvp.Value.Select(route => kvp.Key + '_' + route)))
        {
            App.RootEndpoints.Add(
                fullRoute,
                App.Services.GetRequiredKeyedService<Func<TContext, Task>>(fullRoute));
        }

        App.CachePipelines(App.Services);
        App.SetPipeline(App.GroupPipeline);

        // Rearrange EmbeddedRoutes
        RearrangeEncodedRoutes(App.EncodedRoutes);
        RearrangeEncodedRoutes(App.RootEncodedRoutes);
    }

    private static void RearrangeEncodedRoutes(Dictionary<string, List<string>> encodedRoutes)
    {
        foreach (var list in encodedRoutes.Select(kvp => kvp.Value))
        {
            // Sort in-place according to the custom rules
            list.Sort(static (a, b) =>
            {
                var aIsWildcard = a.Contains('*');
                var bIsWildcard = b.Contains('*');

                // Non-wildcards before wildcards
                if (aIsWildcard != bIsWildcard)
                    return aIsWildcard ? 1 : -1;

                // If both are wildcards, longer first (desc)
                if (!aIsWildcard || !bIsWildcard) 
                    return string.CompareOrdinal(a, b);
                
                var lenDiff = b.Length - a.Length;
                return lenDiff != 0 ? 
                    lenDiff :
                    // Fallback: alphabetical for deterministic order
                    string.CompareOrdinal(a, b);
            });
        }
    }

    private static void DefaultLoggingBuilder(ILoggingBuilder loggingBuilder)
    {
        loggingBuilder
            .ClearProviders()
#if DEBUG
            .SetMinimumLevel(LogLevel.Debug)
#else
            .SetMinimumLevel(LogLevel.Information)
#endif
            .AddConsole();
    }

    /// <summary>
    /// Enables TLS for the server with the specified SSL configuration.
    /// </summary>
    /// <param name="sslServerAuthenticationOptions">The TLS server configuration options.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> UseTls(SslServerAuthenticationOptions sslServerAuthenticationOptions)
    {
        App.TlsEnabled = true;
        App.SslServerAuthenticationOptions = sslServerAuthenticationOptions;

        return this;
    }

    /// <summary>
    /// Sets the server to listen on the given IP address and port.
    /// </summary>
    public Builder<THandler, TContext> Endpoint(IPAddress ipAddress, int port)
    {
        App.IpAddress = ipAddress;
        App.Port = port;

        return this;
    }

    /// <summary>
    /// Sets the server to listen on the given IP address (string) and port.
    /// </summary>
    public Builder<THandler, TContext> Endpoint(string ipAddress, int port)
    {
        App.IpAddress = IPAddress.Parse(ipAddress);
        App.Port = port;

        return this;
    }

    /// <summary>
    /// Sets the TCP port number the server will bind to.
    /// </summary>
    public Builder<THandler, TContext> Port(int port)
    {
        App.Port = port;

        return this;
    }

    /// <summary>
    /// Sets the maximum socket backlog size for pending connections.
    /// </summary>
    public Builder<THandler, TContext> Backlog(int backlog)
    {
        App.Backlog = backlog;

        return this;
    }
    
    /// <summary>
    /// Do not need a scoped provider, boosts performance
    /// </summary>
    public Builder<THandler, TContext> NoScopedEndpoints()
    {
        App.ScopedEndpoints = false;

        return this;
    }
    
    /// <summary>
    /// Use root endpoints and middleware, cannot create rout groups. Use this configuration for very simple
    /// webserver that does not need group routes.
    /// </summary>
    public Builder<THandler, TContext> UseRootEndpoints()
    {
        App.UseRootOnlyEndpoints = true;

        return this;
    }

    /// <summary>
    /// Replaces the default <see cref="IServiceCollection"/> with a custom one.
    /// </summary>
    public Builder<THandler, TContext> EmbedServices(IServiceCollection services)
    {
        App.ServiceCollection = services;

        return this;
    }

    /// <summary>
    /// Registers static files to be served from the given base route.
    /// </summary>
    /// <param name="baseRoute">Route prefix (e.g. <c>"/static"</c>).</param>
    /// <param name="location">Source location (file system or embedded).</param>
    internal Builder<THandler, TContext> ServeStaticFiles(string baseRoute, Location location)
    {
        WiredApp<TContext>.StaticResourceRouteToLocation.Add(baseRoute, location);
        App.CanServeStaticFiles = true;
        return this;
    }

    /// <summary>
    /// Registers SPA assets and enables history-API fallback.
    /// </summary>
    /// <param name="baseRoute">Base route for the SPA (e.g. <c>"/app"</c>).</param>
    /// <param name="location">SPA asset source (embedded or file system).</param>
    internal Builder<THandler, TContext> ServeSpaFiles(string baseRoute, Location location)
    {
        App.CanServeSpaFiles = true;
        return ServeStaticFiles(baseRoute, location);
    }

    /// <summary>
    /// Registers MPA assets under the given base route.
    /// </summary>
    /// <param name="baseRoute">Route prefix for the MPA (e.g. <c>"/site"</c>).</param>
    /// <param name="location">MPA asset source.</param>
    internal Builder<THandler, TContext> ServeMpaFiles(string baseRoute, Location location)
    {
        App.CanServeMpaFiles = true;
        return ServeStaticFiles(baseRoute, location);
    }
}
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Reflection;
using Wired.IO.App;
using Wired.IO.Http11.Middleware;
using Wired.IO.Mediator;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;
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
    public void Initialize(Func<THandler> handlerFactory, List<SslApplicationProtocol> sslApplicationProtocols)
    {
        _registrar = new EndpointRegistrar(App.ServiceCollection);
        _root = new Group(prefix: "/", parent: null, registrar: _registrar);

        App.HttpHandler = handlerFactory();
        App.SslServerAuthenticationOptions.ApplicationProtocols = sslApplicationProtocols;
    }

    /// <summary>
    /// Finalizes the builder and constructs a <see cref="WiredApp{TContext}"/> instance.
    /// Also builds the service provider and resolves all route handlers and middleware.
    /// </summary>
    /// <param name="serviceProvider">
    /// Optional external service provider. If not provided, a default one is built from the <see cref="ServiceCollection"/>.
    /// </param>
    /// <returns>The configured application instance.</returns>
    public WiredApp<TContext> Build(IServiceProvider? serviceProvider = null!)
    {
        var isLoggerFactoryRegistered = App.ServiceCollection.Any(
            d => d.ServiceType == typeof(ILoggerFactory));

        if(!isLoggerFactoryRegistered)
            App.ServiceCollection.AddLogging(DefaultLoggingBuilder);

        App.Services = serviceProvider ?? 
                       App.ServiceCollection.BuildServiceProvider();

        App.LoggerFactory = App.Services.GetRequiredService<ILoggerFactory>();
        App.Logger = App.LoggerFactory.CreateLogger<WiredApp<TContext>>();

        App.Middleware = App.Services.GetServices<Func<TContext, Func<TContext, Task>, Task>>().ToList();
        App.BuildPipeline(App.Middleware, App.EndpointInvoker);

        App.Endpoints = [];

        foreach (var fullRoute in App.EncodedRoutes.SelectMany(kvp => kvp.Value.Select(route => kvp.Key + '_' + route)))
        {
            App.Endpoints.Add(
                fullRoute,
                App.Services.GetRequiredKeyedService<Func<TContext, Task>>(fullRoute));
        }

        return App;
    }

    public WiredApp<TContext> Build2(IServiceProvider? serviceProvider = null!)
    {
        var isLoggerFactoryRegistered = App.ServiceCollection.Any(
            d => d.ServiceType == typeof(ILoggerFactory));

        if (!isLoggerFactoryRegistered)
            App.ServiceCollection.AddLogging(DefaultLoggingBuilder);

        var compiledRoutes = Compile();

        compiledRoutes.PopulateEncodedRoutes(App.EncodedRoutes);
        App.SetCompiledRoutes(compiledRoutes);

        //App.Middleware = App.Services.GetServices<Func<TContext, Func<TContext, Task>, Task>>().ToList();
        //App.BuildPipeline(App.Middleware, App.EndpointInvoker);

        //App.Endpoints = [];

        //foreach (var fullRoute in App.EncodedRoutes.SelectMany(kvp => kvp.Value.Select(route => kvp.Key + '_' + route)))
        //{
        //    App.Endpoints.Add(
        //        fullRoute,
        //        App.Services.GetRequiredKeyedService<Func<TContext, Task>>(fullRoute));
        //}

        App.Services = serviceProvider ??
                       App.ServiceCollection.BuildServiceProvider();

        App.LoggerFactory = App.Services.GetRequiredService<ILoggerFactory>();
        App.Logger = App.LoggerFactory.CreateLogger<WiredApp<TContext>>();

        return App;
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
    /// Replaces the default <see cref="IServiceCollection"/> with a custom one,
    /// and injects default middleware for the current context type.
    /// </summary>
    public Builder<THandler, TContext> EmbedServices(IServiceCollection services)
    {
        App.ServiceCollection = services;
        App.ServiceCollection.AddDefaultMiddleware<TContext>();

        return this;
    }

    /// <summary>
    /// Registers request handlers from the specified assembly and sets up a dispatcher service.
    /// </summary>
    /// <param name="assembly">The assembly to scan for handler definitions.</param>
    public Builder<THandler, TContext> AddHandlers(Assembly assembly)
    {
        App.ServiceCollection
            .AddHandlers(assembly, App)
            .AddScoped<IRequestDispatcher<TContext>, RequestDispatcher<TContext>>();

        return this;
    }

    /// <summary>
    /// Enables automatic dispatch of domain events ("WiredEvents") after each request completes.
    /// </summary>
    /// <param name="dispatchContextWiredEvents">
    /// If <c>true</c>, will dispatch events stored in <see cref="IBaseContext{TRequest,TResponse}.WiredEvents"/>.
    /// </param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> AddWiredEvents(bool dispatchContextWiredEvents = true) 
    {
        App.ServiceCollection.AddWiredEventDispatcher();

        if (!dispatchContextWiredEvents)
            return this;

        UseMiddleware(scope => async (context, next) =>
        {
            await next(context);

            if (context is IHasWiredEvents hasWiredEvents)
            {
                var wiredEventDispatcher = scope.GetRequiredService<Func<IEnumerable<IWiredEvent>, Task>>();

                await wiredEventDispatcher(hasWiredEvents.WiredEvents);
                hasWiredEvents.ClearWiredEvents();
            } 
        });

        return this;
    }

    /// <summary>
    /// Registers static files to be served from the given base route.
    /// </summary>
    /// <param name="baseRoute">Route prefix (e.g. <c>"/static"</c>).</param>
    /// <param name="location">Source location (file system or embedded).</param>
    internal Builder<THandler, TContext> ServeStaticFiles(string baseRoute, Location location)
    {
        App.StaticResourceRouteToLocation.Add(baseRoute, location);
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
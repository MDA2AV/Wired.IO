using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Reflection;
using Wired.IO.App;
using Wired.IO.Http11.Middleware;
using Wired.IO.Http11.Response;
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
public sealed class Builder<THandler, TContext>
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


    // ========== MIDDLEWARE ==========


    /// <summary>
    /// Adds scoped request middleware that can intercept and process requests.
    /// </summary>
    public Builder<THandler, TContext> UseMiddleware(Func<IServiceProvider, Func<TContext, Func<TContext, Task>, Task>> func)
    {
        App.ServiceCollection.AddScoped<Func<TContext, Func<TContext, Task>, Task>>(func);

        return this;
    }
    /// <summary>
    /// Adds response-producing middleware to the pipeline (e.g., filters or short-circuits).
    /// </summary>
    public Builder<THandler, TContext> UseMiddleware(Func<IServiceProvider, Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>> func)
    {
        App.ServiceCollection.AddScoped<Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>>(func);

        return this;
    }


    // ========== Route Mappers ==========


    // ======== FlowControl ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP FlowControl requests for the specified route.
    /// </summary>
    /// <param name="route">The FlowControl route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapFlowControl(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, "FlowControl", route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP FlowControl requests for the specified route.
    /// </summary>
    /// <param name="route">The FlowControl route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapFlowControl(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, "FlowControl", route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP FlowControl requests.
    /// </summary>
    /// <param name="route">The FlowControl route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapFlowControl(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, "FlowControl", route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP FlowControl requests.
    /// </summary>
    /// <param name="route">The FlowControl route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapFlowControl(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, "FlowControl", route);

        return this;
    }


    //=========== GET ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP GET requests for the specified route.
    /// </summary>
    /// <param name="route">The GET route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapGet(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Get, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP GET requests for the specified route.
    /// </summary>
    /// <param name="route">The GET route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapGet(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Get, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP GET requests.
    /// </summary>
    /// <param name="route">The GET route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapGet(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        /*AddKeyedScoped(func, HttpConstants.Get, route);
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Get, route);*/

        AddKeyedScoped(func, HttpConstants.Get, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP GET requests.
    /// </summary>
    /// <param name="route">The GET route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapGet(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Get , route);

        return this;
    }


    //========== POST ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP POST requests for the specified route.
    /// </summary>
    /// <param name="route">The POST route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPost(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Post, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP POST requests for the specified route.
    /// </summary>
    /// <param name="route">The POST route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPost(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Post, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP POST requests.
    /// </summary>
    /// <param name="route">The POST route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPost(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Post, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP POST requests.
    /// </summary>
    /// <param name="route">The POST route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPost(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Post, route);

        return this;
    }


    //========== PUT ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP PUT requests for the specified route.
    /// </summary>
    /// <param name="route">The PUT route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPut(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Put, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP PUT requests for the specified route.
    /// </summary>
    /// <param name="route">The PUT route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPut(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Put, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP PUT requests.
    /// </summary>
    /// <param name="route">The PUT route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPut(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Put, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP PUT requests.
    /// </summary>
    /// <param name="route">The PUT route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPut(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Put, route);

        return this;
    }


    //========== DELETE ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP DELETE requests for the specified route.
    /// </summary>
    /// <param name="route">The DELETE route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapDelete(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Delete, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP DELETE requests for the specified route.
    /// </summary>
    /// <param name="route">The DELETE route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapDelete(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Delete, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP DELETE requests.
    /// </summary>
    /// <param name="route">The DELETE route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapDelete(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Delete, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP DELETE requests.
    /// </summary>
    /// <param name="route">The DELETE route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapDelete(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Delete, route);

        return this;
    }


    //========== PATCH ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP PATCH requests for the specified route.
    /// </summary>
    /// <param name="route">The PATCH route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPatch(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Patch, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP PATCH requests for the specified route.
    /// </summary>
    /// <param name="route">The PATCH route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPatch(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Patch, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP PATCH requests.
    /// </summary>
    /// <param name="route">The PATCH route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPatch(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Patch, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP PATCH requests.
    /// </summary>
    /// <param name="route">The PATCH route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapPatch(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Patch, route);

        return this;
    }


    //========== HEAD ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP HEAD requests for the specified route.
    /// </summary>
    /// <param name="route">The HEAD route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapHead(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Head, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP HEAD requests for the specified route.
    /// </summary>
    /// <param name="route">The HEAD route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapHead(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Head, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP HEAD requests.
    /// </summary>
    /// <param name="route">The HEAD route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapHead(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Head, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP HEAD requests.
    /// </summary>
    /// <param name="route">The HEAD route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapHead(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Head, route);

        return this;
    }


    //========== OPTIONS ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP OPTIONS requests for the specified route.
    /// </summary>
    /// <param name="route">The OPTIONS route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapOptions(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Options, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP OPTIONS requests for the specified route.
    /// </summary>
    /// <param name="route">The OPTIONS route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapOptions(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Options, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP OPTIONS requests.
    /// </summary>
    /// <param name="route">The OPTIONS route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapOptions(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Options, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP OPTIONS requests.
    /// </summary>
    /// <param name="route">The OPTIONS route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapOptions(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Options, route);

        return this;
    }


    //========== TRACE ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP TRACE requests for the specified route.
    /// </summary>
    /// <param name="route">The TRACE route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapTrace(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Trace, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP TRACE requests for the specified route.
    /// </summary>
    /// <param name="route">The TRACE route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapTrace(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Trace, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP TRACE requests.
    /// </summary>
    /// <param name="route">The TRACE route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapTrace(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Trace, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP TRACE requests.
    /// </summary>
    /// <param name="route">The TRACE route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapTrace(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Trace, route);

        return this;
    }


    //========== CONNECT ==========


    /// <summary>
    /// Maps a synchronous <see cref="Action{TContext}"/> to handle HTTP CONNECT requests for the specified route.
    /// </summary>
    /// <param name="route">The CONNECT route to register (e.g., "/api/upload").</param>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapConnect(string route, Action<TContext> func)
    {
        AddKeyedScoped(func, HttpConstants.Connect, route);

        return this;
    }
    /// <summary>
    /// Maps an asynchronous <see cref="Func{TContext, Task}"/> to handle HTTP CONNECT requests for the specified route.
    /// </summary>
    /// <param name="route">The CONNECT route to register (e.g., "/api/create").</param>
    /// <param name="func">An asynchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapConnect(string route, Func<TContext, Task> func)
    {
        AddKeyedScoped(func, HttpConstants.Connect, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces a synchronous <see cref="Action{TContext}"/> for handling HTTP CONNECT requests.
    /// </summary>
    /// <param name="route">The CONNECT route to register (e.g., "/api/process").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapConnect(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Connect, route);

        return this;
    }
    /// <summary>
    /// Maps a route to a handler factory that resolves dependencies per request scope
    /// and produces an asynchronous <see cref="Func{TContext, Task}"/> for handling HTTP CONNECT requests.
    /// </summary>
    /// <param name="route">The CONNECT route to register (e.g., "/api/submit").</param>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns an asynchronous handler 
    /// for the given <typeparamref name="TContext"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> MapConnect(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Connect, route);

        return this;
    }


    /*
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP POST endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapPost(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Post, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP POST endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapPost(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Post, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP PUT endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapPut(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Put, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP PUT endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapPut(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Put, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP DELETE endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapDelete(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Delete, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP DELETE endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapDelete(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Delete, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP PATCH endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapPatch(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Patch, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP PATCH endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapPatch(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Patch, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP HEAD endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapHead(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Head, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP HEAD endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapHead(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Head, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP OPTIONS endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapOptions(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Options, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP OPTIONS endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapOptions(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Options, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP TRACE endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapTrace(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Trace, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP TRACE endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapTrace(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Trace, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP CONNECT endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapConnect(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Connect, route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP CONNECT endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapConnect(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        AddKeyedScoped(func, HttpConstants.Connect, route);

        return this;
    }
    */



    /// <summary>
    /// Registers a keyed scoped service for a route that resolves to a handler factory function.
    /// The factory returns a <see cref="Func{TContext, Task}"/> which processes requests asynchronously.
    /// </summary>
    /// <param name="func">A factory that takes an <see cref="IServiceProvider"/> and returns an async handler function for <typeparamref name="TContext"/>.</param>
    /// <param name="httpMethod">The HTTP method associated with this route (e.g., "GET", "POST").</param>
    /// <param name="route">The route pattern for which this handler is registered (e.g., "/api/users").</param>
    private void AddKeyedScoped(Func<IServiceProvider, Func<TContext, Task>> func, string httpMethod, string route)
    {
        var fullRoute = $"{httpMethod}_{route}";
        App.EncodedRoutes[httpMethod].Add(route);

        App.ServiceCollection.AddKeyedScoped<Func<TContext, Task>>(fullRoute, (sp, key) => func(sp));
    }
    /// <summary>
    /// Registers a keyed scoped service for a route that resolves to a synchronous action factory.
    /// The factory returns an <see cref="Action{TContext}"/> which is wrapped into an asynchronous handler
    /// to maintain a consistent task-based execution model.
    /// </summary>
    /// <param name="func">A factory that takes an <see cref="IServiceProvider"/> and returns a synchronous action handler for <typeparamref name="TContext"/>.</param>
    /// <param name="httpMethod">The HTTP method associated with this route (e.g., "GET", "POST").</param>
    /// <param name="route">The route pattern for which this handler is registered (e.g., "/api/status").</param>
    private void AddKeyedScoped(Func<IServiceProvider, Action<TContext>> func, string httpMethod, string route)
    {
        var fullRoute = $"{httpMethod}_{route}";
        App.EncodedRoutes[httpMethod].Add(route);

        App.ServiceCollection.AddKeyedScoped<Func<TContext, Task>>(fullRoute, (sp, key) =>
        {
            return (ctx) =>
            {
                Action<TContext> action = func(sp);
                action(ctx);
                return Task.CompletedTask;
            };
        });
    }
    /// <summary>
    /// Registers a keyed scoped service for a route using a pre-defined asynchronous handler.
    /// This overload directly binds an existing <see cref="Func{TContext, Task}"/> to the route,
    /// bypassing the need for dependency resolution.
    /// </summary>
    /// <param name="func">An async handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <param name="httpMethod">The HTTP method associated with this route.</param>
    /// <param name="route">The route pattern for which this handler is registered.</param>
    private void AddKeyedScoped(Func<TContext, Task> func, string httpMethod, string route)
    {
        var fullRoute = $"{httpMethod}_{route}";
        App.EncodedRoutes[httpMethod].Add(route);

        App.ServiceCollection.AddKeyedScoped<Func<TContext, Task>>(fullRoute, (_, _) => func);
    }
    /// <summary>
    /// Registers a keyed scoped service for a route using a pre-defined synchronous handler.
    /// The handler is automatically wrapped into an asynchronous function returning a completed task.
    /// </summary>
    /// <param name="func">A synchronous handler that processes the given <typeparamref name="TContext"/>.</param>
    /// <param name="httpMethod">The HTTP method associated with this route.</param>
    /// <param name="route">The route pattern for which this handler is registered.</param>
    private void AddKeyedScoped(Action<TContext> func, string httpMethod, string route)
    {
        var fullRoute = $"{httpMethod}_{route}";
        App.EncodedRoutes[httpMethod].Add(route);

        App.ServiceCollection.AddKeyedScoped<Func<TContext, Task>>(fullRoute, (_, _) =>
        {
            return (ctx) =>
            {
                func(ctx);
                return Task.CompletedTask;
            };
        });
    }
}
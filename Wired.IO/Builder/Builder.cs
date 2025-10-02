using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.App;
using Wired.IO.Mediator;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;
using IBaseRequest = Wired.IO.Protocol.Request.IBaseRequest;
using Wired.IO.Http11.Response;
using Wired.IO.Http11.Middleware;

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

    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP GET endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapGet(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Get , route);

        return this;
    }
    /// <summary>
    /// Maps a <see cref="Func{TContext, Task}"/> delegate to an HTTP GET endpoint for the specified route.
    /// </summary>
    /// <param name="route">The route pattern (e.g. <c>/users/:id</c>).</param>
    /// <param name="func">A factory that produces a scoped request handler delegate.</param>
    /// <returns>The current builder instance.</returns>
    public Builder<THandler, TContext> MapGet(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Get, route);

        return this;
    }
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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Post, route);

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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Put, route);

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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Delete, route);

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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Patch, route);

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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Head, route);

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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Options, route);

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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Trace, route);

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
        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        AddKeyedScoped(AsyncFunc, HttpConstants.Connect, route);

        return this;
    }

    /// <summary>
    /// Registers a handler function under a keyed service and adds the route to the encoded route map.
    /// </summary>
    private void AddKeyedScoped(Func<IServiceProvider, Func<TContext, Task>> func, string httpMethod, string route)
    {
        var fullRoute = $"{httpMethod}_{route}";
        App.EncodedRoutes[httpMethod].Add(route);

        App.ServiceCollection.AddKeyedScoped<Func<TContext, Task>>(fullRoute, (sp, key) => func(sp));
    }
}
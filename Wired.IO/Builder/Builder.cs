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

namespace Wired.IO.Builder;

public sealed class Builder<THandler, TContext>
    where THandler : IHttpHandler<TContext>
    where TContext : IContext
{
    public Builder(Func<THandler> handlerFactory)
    {
        App.HttpHandler = handlerFactory();
        App.SslServerAuthenticationOptions.ApplicationProtocols = [SslApplicationProtocol.Http11];
    }

    public Builder(Func<THandler> handlerFactory, List<SslApplicationProtocol> sslApplicationProtocols)
    {
        App.HttpHandler = handlerFactory();
        App.SslServerAuthenticationOptions.ApplicationProtocols = sslApplicationProtocols;
    }

    public App<TContext> App { get; } = new App<TContext>();

    public App<TContext> Build()
    {
        App.InternalHost = App.HostBuilder.Build();
        App.LoggerFactory = App.InternalHost.Services.GetRequiredService<ILoggerFactory>();
        App.Logger = App.LoggerFactory.CreateLogger<App<TContext>>();
        App.Middleware = App.InternalHost.Services.GetServices<Func<TContext, Func<TContext, Task>, Task>>().ToList();

        App.Endpoints = [];
        foreach (var route in App.Routes)
        {
            Console.WriteLine(route);
            App.Endpoints.Add(
                route, 
                App.InternalHost.Services.GetRequiredKeyedService<Func<TContext, Task>>(route));
        }

        return App;
    }

    public Builder<THandler, TContext> UseTls(SslServerAuthenticationOptions sslServerAuthenticationOptions)
    {
        App.TlsEnabled = true;
        App.SslServerAuthenticationOptions = sslServerAuthenticationOptions;

        return this;
    }

    public Builder<THandler, TContext> Endpoint(IPAddress ipAddress, int port)
    {
        App.IpAddress = ipAddress;
        App.Port = port;

        return this;
    }

    public Builder<THandler, TContext> Endpoint(string ipAddress, int port)
    {
        App.IpAddress = IPAddress.Parse(ipAddress);
        App.Port = port;

        return this;
    }

    public Builder<THandler, TContext> Port(int port)
    {
        App.Port = port;

        return this;
    }

    public Builder<THandler, TContext> Backlog(int backlog)
    {
        App.Backlog = backlog;

        return this;
    }

    public Builder<THandler, TContext> AddHandlers(Assembly assembly)
    {
        App.HostBuilder.ConfigureServices((_, services) =>
        {
            services
                .AddHandlers(assembly, App)
                .AddScoped<IRequestDispatcher<TContext>, RequestDispatcher<TContext>>();
        });

        return this;
    }

    public Builder<THandler, TContext> AddWiredEvents(bool dispatchContextWiredEvents = true)
    {
        App.HostBuilder.ConfigureServices((_, services) =>
        {
            services.AddWiredEventDispatcher();
        });

        if (!dispatchContextWiredEvents)
            return this;

        UseMiddleware(scope => async (context, next) =>
        {
            await next(context);

            var wiredEventDispatcher = scope.GetRequiredService<Func<IEnumerable<IWiredEvent>, Task>>();

            await wiredEventDispatcher(context.WiredEvents);
            context.ClearWiredEvents();
        });

        return this;
    }

    public Builder<THandler, TContext> UseMiddleware(Func<IServiceProvider, Func<TContext, Func<TContext, Task>, Task>> func)
    {
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddScoped<Func<TContext, Func<TContext, Task>, Task>>(func));

        return this;
    }
    public Builder<THandler, TContext> UseMiddleware(Func<IServiceProvider, Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>> func)
    {
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddScoped<Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>>(func));

        return this;
    }

    public Builder<THandler, TContext> MapGet(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        AddKeyedScoped(func, HttpConstants.Get , route);

        App.EncodedRoutes[HttpConstants.Get].Add(route);

        /*
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Get}_{route}", (sp, key) => func(sp)));
        */

        return this;
    }
    public Builder<THandler, TContext> MapGet(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        App.EncodedRoutes[HttpConstants.Get].Add(route);

        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Get}_{route}", (sp, key) => AsyncFunc(sp)));

        return this;
    }

    public Builder<THandler, TContext> MapPost(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        App.EncodedRoutes[HttpConstants.Post].Add(route);
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Post}_{route}", (sp, key) => func(sp)));

        return this;
    }
    public Builder<THandler, TContext> MapPost(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        App.EncodedRoutes[HttpConstants.Get].Add(route);

        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Post}_{route}", (sp, key) => AsyncFunc(sp)));

        return this;
    }

    public Builder<THandler, TContext> MapPut(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        App.EncodedRoutes[HttpConstants.Put].Add(route);
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Put}_{route}", (sp, key) => func(sp)));

        return this;
    }
    public Builder<THandler, TContext> MapPut(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        App.EncodedRoutes[HttpConstants.Get].Add(route);

        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Put}_{route}", (sp, key) => AsyncFunc(sp)));

        return this;
    }

    public Builder<THandler, TContext> MapDelete(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        App.EncodedRoutes[HttpConstants.Delete].Add(route);
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Delete}_{route}", (sp, key) => func(sp)));

        return this;
    }
    public Builder<THandler, TContext> MapDelete(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        App.EncodedRoutes[HttpConstants.Get].Add(route);

        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Delete}_{route}", (sp, key) => AsyncFunc(sp)));

        return this;
    }

    public Builder<THandler, TContext> MapPatch(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        App.EncodedRoutes[HttpConstants.Patch].Add(route);
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Patch}_{route}", (sp, key) => func(sp)));

        return this;
    }
    public Builder<THandler, TContext> MapPatch(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        App.EncodedRoutes[HttpConstants.Get].Add(route);

        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Patch}_{route}", (sp, key) => AsyncFunc(sp)));

        return this;
    }

    public Builder<THandler, TContext> MapHead(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        App.EncodedRoutes[HttpConstants.Head].Add(route);
        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Head}_{route}", (sp, key) => func(sp)));

        return this;
    }
    public Builder<THandler, TContext> MapHead(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        App.EncodedRoutes[HttpConstants.Get].Add(route);

        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Head}_{route}", (sp, key) => AsyncFunc(sp)));

        return this;
    }

    public Builder<THandler, TContext> MapOptions(string route, Func<IServiceProvider, Func<TContext, Task>> func)
    {
        App.EncodedRoutes[HttpConstants.Options].Add(route);

        //AddKeyedScoped(func, $"{HttpConstants.Options}_{route}");

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Options}_{route}", (sp, key) => func(sp)));

        return this;
    }
    public Builder<THandler, TContext> MapOptions(string route, Func<IServiceProvider, Action<TContext>> func)
    {
        App.EncodedRoutes[HttpConstants.Get].Add(route);

        Func<TContext, Task> AsyncFunc(IServiceProvider sp)
        {
            Action<TContext> action = func(sp);
            return context =>
            {
                action(context);

                return Task.CompletedTask;
            };
        }

        //AddKeyedScoped(AsyncFunc, $"{HttpConstants.Options}_{route}");

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>($"{HttpConstants.Options}_{route}", (sp, key) => AsyncFunc(sp)));

        return this;
    }

    private void AddKeyedScoped(Func<IServiceProvider, Func<TContext, Task>> func, string httpMethod, string route)
    {
        var fullRoute = $"{httpMethod}_{route}";
        App.Routes.Add(fullRoute);

        App.HostBuilder.ConfigureServices((_, services) =>
            services.AddKeyedScoped<Func<TContext, Task>>
                (fullRoute, (sp, key) => func(sp)));
    }
}
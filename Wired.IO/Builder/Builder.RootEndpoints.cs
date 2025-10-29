using Microsoft.Extensions.DependencyInjection;
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
        AddKeyedScoped(func, HttpConstants.Get, route);

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
        App.RootEncodedRoutes[httpMethod].Add(route);

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
        App.RootEncodedRoutes[httpMethod].Add(route);

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
        App.RootEncodedRoutes[httpMethod].Add(route);

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
        App.RootEncodedRoutes[httpMethod].Add(route);

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
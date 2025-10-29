using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Http11.Response;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Builder;

public sealed partial class Builder<THandler, TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    where THandler : IHttpHandler<TContext>
{
    /// <summary>
    /// Registers a middleware component that resolves dependencies per request scope
    /// and executes logic before or after the next middleware in the pipeline.
    /// </summary>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a middleware delegate of type
    /// <see cref="Func{TContext, Func{TContext, Task}, Task}"/>.  
    /// The inner delegate represents the next middleware in the pipeline.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> UseRootMiddleware(Func<IServiceProvider, Func<TContext, Func<TContext, Task>, Task>> func)
    {
        App.ServiceCollection.AddScoped<Func<TContext, Func<TContext, Task>, Task>>(func);

        return this;
    }
    /// <summary>
    /// Registers a middleware component that does not require dependency injection.
    /// </summary>
    /// <param name="func">
    /// A middleware delegate of type <see cref="Func{TContext, Func{TContext, Task}, Task}"/>.  
    /// The first parameter represents the current request context, and the second parameter is the
    /// next middleware to invoke in the pipeline.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> UseRootMiddleware(Func<TContext, Func<TContext, Task>, Task> func)
    {
        App.ServiceCollection.AddScoped<Func<TContext, Func<TContext, Task>, Task>>(_ => func);

        return this;
    }
    /// <summary>
    /// Registers a middleware component that resolves dependencies per request scope
    /// and supports returning an <see cref="IResponse"/> from the middleware chain.
    /// </summary>
    /// <param name="func">
    /// A factory that takes an <see cref="IServiceProvider"/> and returns a middleware delegate of type
    /// <see cref="Func{TContext, Func{TContext, Task{IResponse}}, Task{IResponse}}"/>.  
    /// The inner delegate represents the next middleware in the pipeline, which also returns an <see cref="IResponse"/>.
    /// </param>
    /// <returns>The current <see cref="Builder{THandler, TContext}"/> instance for chaining.</returns>
    public Builder<THandler, TContext> UseRootMiddleware(Func<IServiceProvider, Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>> func)
    {
        App.ServiceCollection.AddScoped<Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>>(func);

        return this;
    }
}
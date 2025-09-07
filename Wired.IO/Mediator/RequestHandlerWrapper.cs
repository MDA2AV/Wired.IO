using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Mediator;

/// <summary>
/// Wrapper that handles a request with a response by executing its associated pipeline behaviors
/// and invoking the appropriate <see cref="IRequestHandler{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
[ExcludeFromCodeCoverage]
public class RequestHandlerWrapper<TRequest, TResponse> : IRequestHandlerWrapper<TResponse>
    where TRequest : class, IRequest<TResponse>
{
    public async Task<TResponse> Handle(IBaseRequest baseRequest, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var request = (TRequest)baseRequest;

        var handler = provider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        var behaviors = provider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToList();

        RequestHandlerDelegate<TResponse> handlerDelegate = () =>
            handler.Handle(request, cancellationToken);

        foreach (var behavior in behaviors.AsEnumerable().Reverse())
        {
            var next = handlerDelegate;
            handlerDelegate = () => behavior.Handle(request, next, cancellationToken);
        }

        return await handlerDelegate();
    }
}

/// <summary>
/// Wrapper that handles context-based pipelines by executing registered <see cref="IPipelineBehavior{TContext}"/>
/// and invoking the appropriate <see cref="IContextHandler{TContext}"/>.
/// </summary>
/// <typeparam name="TContext">The type of the context used in the pipeline.</typeparam>
[ExcludeFromCodeCoverage]
public class ContextHandlerWrapper<TContext> : IContextHandlerWrapper<TContext>
    where TContext : class, IBaseContext<Protocol.Request.IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Executes the pipeline for the given context by resolving the handler and applying all registered behaviors.
    /// </summary>
    /// <param name="context">The context to be processed.</param>
    /// <param name="provider">The service provider used to resolve the handler and behaviors.</param>
    /// <param name="cancellationToken">A cancellation token to observe during execution.</param>
    /// <returns>A task that completes when the context handler and all behaviors have executed.</returns>
    public async Task Handle(TContext context, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var handler = provider.GetRequiredService<IContextHandler<TContext>>();
        var behaviors = provider.GetServices<IPipelineBehavior<TContext>>().ToList();

        RequestHandlerDelegate handlerDelegate = () =>
            handler.Handle(context, cancellationToken);

        foreach (var behavior in behaviors.AsEnumerable().Reverse())
        {
            var next = handlerDelegate;
            handlerDelegate = () => behavior.Handle(context, next, cancellationToken);
        }

        await handlerDelegate();
    }
}

/// <summary>
/// Wrapper that handles a request without a response (void-style)
/// by executing its associated pipeline behaviors and invoking the appropriate <see cref="IRequestHandler{TRequest}"/>.
/// </summary>
/// <typeparam name="TRequest">The type of the request.</typeparam>
[ExcludeFromCodeCoverage]
public class RequestHandlerWrapper<TRequest> : IRequestHandlerWrapper
    where TRequest : class, IRequest
{
    public async Task Handle(IBaseRequest baseRequest, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var request = (TRequest)baseRequest;
        var handler = provider.GetRequiredService<IRequestHandler<TRequest>>();
        var behaviors = provider.GetServices<IPipelineBehaviorNoResponse<TRequest>>().ToList();

        RequestHandlerDelegate handlerDelegate = async () =>
        {
            await handler.Handle(request, cancellationToken);
        };

        foreach (var behavior in behaviors.AsEnumerable().Reverse())
        {
            var next = handlerDelegate;
            handlerDelegate = () => behavior.Handle(request, next, cancellationToken);
        }

        await handlerDelegate();
    }
}

public readonly struct Unit
{
    public static readonly Unit Value = new();
}
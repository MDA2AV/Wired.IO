using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using Wired.IO.Protocol;

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

[ExcludeFromCodeCoverage]
public class ContextHandlerWrapper<TContext> : IContextHandlerWrapper<TContext>
    where TContext : class, IContext
{
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
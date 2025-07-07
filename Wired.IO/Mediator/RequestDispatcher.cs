using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Wired.IO.Http11.Context;
using Wired.IO.Protocol;

namespace Wired.IO.Mediator;

/// <summary>
/// Default implementation of <see cref="IRequestDispatcher{TContext}"/> that orchestrates the execution
/// of requests through a pipeline of behaviors.
/// 
/// This implementation uses a static cache to avoid repeated reflection costs when creating
/// handler wrappers, while still resolving actual handler implementations and pipeline behaviors
/// from the dependency injection container for each request.
/// </summary>
/// <remarks>
/// The dispatcher works by:
/// 1. Creating type-specific wrappers that know how to resolve and execute the appropriate handler
/// 2. Caching these wrappers to avoid reflection overhead on subsequent calls
/// 3. Using the provided service provider to resolve handler instances and pipeline behaviors at runtime
/// 
/// This approach allows handlers and behaviors to be properly scoped services while avoiding
/// the performance cost of repeated reflection.
/// </remarks>
[ExcludeFromCodeCoverage]
public class RequestDispatcher<TContext>(IServiceProvider serviceProvider) : IRequestDispatcher<TContext>
    where TContext : IContext
{
    /// <summary>
    /// Static cache of request handler wrappers to avoid repeated reflection costs.
    /// The key is a tuple of (RequestType, ResponseType?) where ResponseType is null for requests without responses.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type?, Type?), object> WrapperCache = new();

    /// <summary>
    /// Sends a request that returns a response through the mediator pipeline.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the request</typeparam>
    /// <param name="request">The request object</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>The response from the request handler</returns>
    /// <remarks>
    /// This method retrieves or creates a wrapper for the specific request/response types,
    /// then uses that wrapper to resolve the actual handler and execute the request through
    /// the pipeline of behaviors.
    /// </remarks>
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        var wrapperObj = WrapperCache.GetOrAdd((requestType, typeof(TResponse)), static key =>
        {
            var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(key.Item1!, key.Item2!);
            return Activator.CreateInstance(wrapperType)!;
        });

        var wrapper = (IRequestHandlerWrapper<TResponse>)wrapperObj;

        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    /// <summary>
    /// Sends a request with no response through the mediator pipeline.
    /// </summary>
    /// <param name="request">The request object</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method handles requests that don't return a response. It follows the same pattern
    /// as the typed version, retrieving or creating a wrapper that knows how to resolve and
    /// execute the appropriate handler through the pipeline of behaviors.
    /// </remarks>
    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        var wrapperObj = WrapperCache.GetOrAdd((requestType, null), static key =>
        {
            var wrapperType = typeof(RequestHandlerWrapper<>).MakeGenericType(key.Item1!);
            return Activator.CreateInstance(wrapperType)!;
        });

        var wrapper = (IRequestHandlerWrapper)wrapperObj;

        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    public Task Send(TContext context, CancellationToken cancellationToken = default)
    {
        var wrapperObj = WrapperCache.GetOrAdd((null, null), static key => 
            Activator.CreateInstance(typeof(ContextHandlerWrapper<>).MakeGenericType(typeof(TContext)))!);

        var wrapper = (IContextHandlerWrapper<TContext>)wrapperObj;

        return wrapper.Handle(context, serviceProvider, cancellationToken);
    }
}
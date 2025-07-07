using Wired.IO.Protocol;

namespace Wired.IO.Mediator;

/// <summary>
/// Defines a handler for a request
/// </summary>
/// <typeparam name="TRequest">The type of request being handled</typeparam>
/// <typeparam name="TResponse">The type of response from the handler</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles a request
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from the request</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a handler for a request with a void response.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled</typeparam>
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    /// <summary>
    /// Handles a request
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from the request</returns>
    Task Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IContextHandler<in TContext>
    where TContext : IContext
{
    /// <summary>
    /// Handles a request
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from the request</returns>
    Task Handle(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a wrapper around a generic request handler with a return value.
/// Used to resolve and invoke the appropriate handler and pipeline from a non-generic context.
/// </summary>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface IRequestHandlerWrapper<TResponse>
{
    /// <summary>
    /// Handles a request by resolving its handler and pipeline from the provided <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="baseRequest">The request to handle.</param>
    /// <param name="provider">The service provider for resolving dependencies.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>A task representing the asynchronous operation, with a result of type <typeparamref name="TResponse"/>.</returns>
    Task<TResponse> Handle(IBaseRequest baseRequest, IServiceProvider provider, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a wrapper around a generic request handler with no return value (void).
/// Used to resolve and invoke the appropriate handler and pipeline from a non-generic context.
/// </summary>
public interface IRequestHandlerWrapper
{
    /// <summary>
    /// Handles a request by resolving its handler and pipeline from the provided <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="baseRequest">The request to handle.</param>
    /// <param name="provider">The service provider for resolving dependencies.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(IBaseRequest baseRequest, IServiceProvider provider, CancellationToken cancellationToken);
}

public interface IContextHandlerWrapper<in TContext>
    where TContext : IContext
{
    /// <summary>
    /// Handles a context by resolving its handler and pipeline from the provided <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="context">The context to handle.</param>
    /// <param name="provider">The service provider for resolving dependencies.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(TContext context, IServiceProvider provider, CancellationToken cancellationToken);
}
namespace Wired.IO.Mediator;

/// <summary>
/// Represents an async continuation for the next task to execute in the pipeline
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
/// <returns>Awaitable task returning a <typeparamref name="TResponse"/></returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public delegate Task RequestHandlerDelegate();

/// <summary>
/// Pipeline behavior to surround the inner handler.
/// Implementations add additional behavior and await the next delegate.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
{
    /// <summary>
    /// Pipeline handler. Perform any additional behavior and await the <paramref name="next"/> delegate as necessary
    /// </summary>
    /// <param name="request">Incoming request</param>
    /// <param name="next">Awaitable delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Awaitable task returning the <typeparamref name="TResponse"/></returns>
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a pipeline behavior for a request that does not return a response.
/// </summary>
/// <typeparam name="TRequest">The type of the incoming request.</typeparam>
public interface IPipelineBehaviorNoResponse<in TRequest> where TRequest : notnull
{
    /// <summary>
    /// Handles the request by optionally adding behavior before or after invoking the <paramref name="next"/> delegate.
    /// </summary>
    /// <param name="request">The incoming request instance.</param>
    /// <param name="next">The delegate representing the next action in the pipeline.</param>
    /// <param name="cancellationToken">A cancellation token to observe during execution.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a pipeline behavior based on a context object, typically used in frameworks where context holds request, response, and services.
/// </summary>
/// <typeparam name="TContext">The context type for the current request.</typeparam>
public interface IPipelineBehavior<in TContext> where TContext : notnull
{
    /// <summary>
    /// Handles the context by optionally adding behavior before or after invoking the <paramref name="next"/> delegate.
    /// </summary>
    /// <param name="context">The context associated with the current request pipeline.</param>
    /// <param name="next">The delegate representing the next step in the pipeline.</param>
    /// <param name="cancellationToken">A cancellation token to observe during execution.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(TContext context, RequestHandlerDelegate next, CancellationToken cancellationToken);
}
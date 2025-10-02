using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Mediator;

/// <summary>
/// Abstraction for a request dispatcher that executes a request 
/// through the configured pipeline (behaviors + handler).
/// </summary>
public interface IRequestDispatcher<in TContext>
    where TContext : IBaseContext<Protocol.Request.IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Sends a request through the pipeline and returns the response.
    /// The pipeline includes any registered <see cref="IPipelineBehavior{TRequest, TResponse}"/> 
    /// and the appropriate <see cref="IRequestHandler{TRequest, TResponse}"/>.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response</typeparam>
    /// <param name="request">The request to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The response from the pipeline</returns>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request through the pipeline that does not produce a response.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Send(IRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the pipeline directly against the provided context, bypassing request/response types.
    /// </summary>
    /// <param name="context">The context to pass through the pipeline.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Send(TContext context, CancellationToken cancellationToken = default);
}
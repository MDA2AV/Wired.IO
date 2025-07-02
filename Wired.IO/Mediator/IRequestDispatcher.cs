namespace Wired.IO.Mediator;

/// <summary>
/// Abstraction for a request dispatcher that executes a request 
/// through the configured pipeline (behaviors + handler).
/// </summary>
public interface IRequestDispatcher
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
    // Void response
    Task Send(IRequest request, CancellationToken cancellationToken = default);
}
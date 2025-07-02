namespace Wired.IO.Protocol.Handlers;

/// <summary>
/// Defines a contract for handling client connections using a custom or HTTP-based protocol.
/// </summary>
public interface IHttpHandler<out TContext>
    where TContext : IContext
{
    /// <summary>
    /// Processes a client connection and dispatches one or more protocol-compliant requests.
    /// </summary>
    /// <param name="stream">The <see cref="Stream"/> representing the client connection.</param>
    /// <param name="pipeline">
    /// A delegate that executes the application's request-handling pipeline, typically consisting of middleware and endpoint logic.
    /// </param>
    /// <param name="stoppingToken">
    /// A <see cref="CancellationToken"/> used to signal cancellation, such as during server shutdown.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation of handling the client session.
    /// </returns>
    Task HandleClientAsync(
        Stream stream,
        Func<TContext, Task> pipeline,
        CancellationToken stoppingToken);
}
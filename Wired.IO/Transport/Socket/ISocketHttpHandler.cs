using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.Socket;

/// <summary>
/// Defines a contract for handling client connections using a custom or HTTP-based protocol.
/// </summary>
public interface ISocketHttpHandler<out TContext> : IHttpHandler
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Processes a client connection and dispatches one or more protocol-compliant requests.
    /// </summary>
    /// <param name="inner"></param>
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
        System.Net.Sockets.Socket inner,
        Stream stream,
        Func<TContext, Task> pipeline,
        CancellationToken stoppingToken);
}
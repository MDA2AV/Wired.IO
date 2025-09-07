using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;

namespace Wired.IO.Protocol;

/// <summary>
/// Represents the context for a client connection, encapsulating the connection details, request, response, and dependency resolution.
/// </summary>
public interface IContext : IBaseContext
{
    /// <summary>
    /// Gets or sets the HTTP request for the current connection.
    /// This property contains all the details of the incoming HTTP request,
    /// such as the request method, headers, URI, and body.
    /// </summary>
    IRequest Request { get; }

    /// <summary>
    /// Gets or sets the HTTP response to be sent back to the client.
    /// This property holds the response data, including status code, headers, and content.
    /// It is constructed and written to the stream after the request has been processed.
    /// </summary>
    IResponse? Response { get; set; }
}

public interface IBaseContext : IHasWiredEvents, IDisposable
{
    /// <summary>
    /// Gets or sets the <see cref="PipeReader"/> used to read incoming data from the client connection.
    /// </summary>
    /// <remarks>
    /// This reader is typically bound to the network stream and used to consume HTTP request data
    /// such as headers, body content, or raw bytes from the client.
    /// </remarks>
    PipeReader Reader { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="PipeWriter"/> used to write outgoing data to the client connection.
    /// </summary>
    /// <remarks>
    /// This writer is used to serialize the HTTP response, including headers and body, and flush it
    /// to the client. It can be wrapped with encoders like chunked or plain writers.
    /// </remarks>
    PipeWriter Writer { get; set; }

    /// <summary>
    /// Gets or sets the service scope for resolving scoped services during the lifecycle of the request.
    /// </summary>
    /// <value>
    /// An instance of <see cref="AsyncServiceScope"/> for managing scoped service lifetimes.
    /// </value>
    AsyncServiceScope Scope { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="CancellationToken"/> for the current context.
    /// </summary>
    /// <remarks>
    /// - Used to monitor for cancellation requests, allowing graceful termination of operations.
    /// - Passed to all asynchronous operations initiated within the context.
    /// </remarks>
    CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Clears the request and response state of the current context without disposing it.
    /// </summary>
    /// <remarks>
    /// This method is typically used to reset the context for reuse within a connection handling loop.
    /// </remarks>
    void Clear();
}
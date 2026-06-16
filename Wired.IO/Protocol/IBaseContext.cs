using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Protocol;

public interface IBaseContext<out TRequest, out TResponse> : IDisposable 
    where TRequest : IBaseRequest
    where TResponse : IBaseResponse
{
    /// <summary>
    /// Gets or sets the HTTP request for the current connection.
    /// This property contains all the details of the incoming HTTP request,
    /// such as the request method, headers, URI, and body.
    /// </summary>
    TRequest Request { get; }

    /// <summary>
    /// Gets or sets the HTTP response to be sent back to the client.
    /// This property holds the response data, including status code, headers, and content.
    /// It is constructed and written to the stream after the request has been processed.
    /// </summary>
    TResponse? Response { get; }

    /// <summary>
    /// Gets or sets the service scope for resolving scoped services during the lifecycle of the request.
    /// </summary>
    /// <value>
    /// An instance of <see cref="AsyncServiceScope"/> for managing scoped service lifetimes.
    /// </value>
    //AsyncServiceScope Scope { get; set; }
    IServiceProvider Services { get; set; }

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
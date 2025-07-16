using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;
using Wired.IO.Http11.Response;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;

namespace Wired.IO.Http11.Context;

/// <summary>
/// Represents the HTTP/1.1-specific implementation of the <see cref="IContext"/> interface.
/// </summary>
/// <remarks>
/// This class manages the lifetime and processing state of a single HTTP/1.1 connection, including its request, response,
/// cancellation state, and DI service resolution scope.
/// </remarks>
public class Http11Context : IContext
{
    /// <summary>
    /// Gets or sets the <see cref="PipeReader"/> used to read data from the client connection.
    /// </summary>
    public PipeReader Reader { get; set; } = null!;

    /// <summary>
    /// Gets or sets the <see cref="PipeWriter"/> used to write data to the client connection.
    /// </summary>
    public PipeWriter Writer { get; set; } = null!;

    /// <summary>
    /// Gets or sets the current HTTP request associated with the connection.
    /// </summary>
    public IRequest Request { get; set; } = null!;

    /// <summary>
    /// Gets or sets the <see cref="AsyncServiceScope"/> for resolving scoped services during the request lifecycle.
    /// </summary>
    public AsyncServiceScope Scope { get; set; }

    /// <summary>
    /// Resolves a scoped service of the specified type from the current <see cref="Scope"/>.
    /// </summary>
    /// <typeparam name="T">The service type to resolve.</typeparam>
    /// <returns>An instance of the requested service type.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the service cannot be resolved.</exception>
    public T Resolve<T>() where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// Gets or sets the response that will be sent back to the client.
    /// </summary>
    public IResponse? Response { get; set; }

    /// <summary>
    /// Creates and initializes a new HTTP/1.1 response using the default OK status.
    /// </summary>
    /// <returns>An <see cref="IResponseBuilder"/> for fluently composing the response.</returns>
    public IResponseBuilder Respond()
    {
        Response = new Http11Response();
        return new ResponseBuilder(Response).Status(ResponseStatus.Ok);
    }

    /// <summary>
    /// Gets or sets the <see cref="CancellationToken"/> associated with the current context,
    /// used to monitor for cancellation of request processing.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    private readonly List<IWiredEvent> _wiredEvents = new();

    public IReadOnlyList<IWiredEvent> WiredEvents => _wiredEvents.AsReadOnly();

    public void AddWiredEvent(IWiredEvent wiredEvent)
    {
        _wiredEvents.Add(wiredEvent);
    }

    public void ClearWiredEvents()
    {
        _wiredEvents.Clear();
    }

    public void Clear()
    {
        Response?.Clear();
        Request.Clear();
    }

    /// <summary>
    /// Disposes the context and its associated resources.
    /// </summary>
    /// <remarks>
    /// - Completes the <see cref="Reader"/> and <see cref="Writer"/>.
    /// - Disposes the response and request objects.
    /// </remarks>
    public void Dispose()
    {
        Reader.Complete();
        Writer.Complete();

        Response?.Dispose();
        Request.Dispose();
    }
}
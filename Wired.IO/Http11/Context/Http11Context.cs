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
    public PipeReader Reader { get; set; } = null!;

    public PipeWriter Writer { get; set; } = null!;

    public IRequest Request { get; set; } = null!;

    public AsyncServiceScope Scope { get; set; }

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

    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// A list of wired events that have been raised during the processing of this context.
    /// </summary>
    /// <remarks>
    /// These events are typically dispatched after the request completes,
    /// enabling integration with event-driven architectures such as the outbox pattern.
    /// </remarks>
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
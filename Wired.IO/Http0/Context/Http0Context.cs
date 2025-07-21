using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;

namespace Wired.IO.Http0.Context;

public sealed class Http0Context : IContext
{
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

    public PipeReader Reader { get; set; } = null!;
    public PipeWriter Writer { get; set; } = null!;
    public IRequest Request { get; set; } = null!;
    public AsyncServiceScope Scope { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public IResponse? Response { get; set; }
    public void Clear()
    {
    }

    public void Dispose()
    {
    }
}
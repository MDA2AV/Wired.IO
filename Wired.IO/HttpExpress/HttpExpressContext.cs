using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;

namespace Wired.IO.HttpExpress;

public class HttpExpressContext : IContext
{
    public IReadOnlyList<IWiredEvent> WiredEvents { get; } = null!;

    public void AddWiredEvent(IWiredEvent wiredEvent)
    {
        // Not used
    }

    public void ClearWiredEvents()
    {
        // Not used
    }

    public PipeReader Reader { get; set; } = null!;
    public PipeWriter Writer { get; set; } = null!;
    public IRequest Request { get; set; } = null!;
    public IResponse? Response { get; set; }

    public AsyncServiceScope Scope { get; set; }
    public CancellationToken CancellationToken { get; set; }


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
        Response?.Dispose();
        Request.Dispose();
    }
}
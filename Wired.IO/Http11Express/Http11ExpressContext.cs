using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express;

public class Http11ExpressContext : IBaseContext<IExpressRequest, IBaseResponse>
{
    public PipeReader Reader { get; set; } = null!;
    public PipeWriter Writer { get; set; } = null!;
    public IExpressRequest Request { get; set; } = new Http11ExpressRequest()
    {
        Headers = new PooledDictionary<string, string>(
            capacity: 8,
            comparer: StringComparer.OrdinalIgnoreCase),
        QueryParameters = new PooledDictionary<string, string>(
            capacity: 2,
            comparer: StringComparer.OrdinalIgnoreCase)
    };
    public IBaseResponse? Response { get; set; }
    public AsyncServiceScope Scope { get; set; }
    public CancellationToken CancellationToken { get; set; }

    public void Clear()
    {
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
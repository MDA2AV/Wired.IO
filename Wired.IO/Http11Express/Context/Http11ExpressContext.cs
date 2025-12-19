using System.IO.Pipelines;
using Wired.IO.Http11Express.Request;
using Wired.IO.Http11Express.Response;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express.Context;

// This class cannot be sealed, might have super types
public class Http11ExpressContext : IBaseContext<IExpressRequest, IExpressResponse>
{
    public PipeReader Reader { get; set; } = null!;
    public PipeWriter Writer { get; set; } = null!;
    public IExpressRequest Request { get; set; } = new Http11ExpressRequest()
    {
        Headers = new PooledDictionary<string, string>(
            capacity: 8,
            comparer: StringComparer.OrdinalIgnoreCase),
        QueryParameters = new PooledDictionary<string, string>(
            capacity: 8,
            comparer: StringComparer.OrdinalIgnoreCase)
    };
    public IExpressResponse? Response { get; private set; }

    private ExpressResponseBuilder? _responseBuilder;
    private ExpressResponseBuilder ResponseBuilder => _responseBuilder ??= new ExpressResponseBuilder(Response!);
    
    public ExpressResponseBuilder Respond()
    {
        Response ??= new Http11ExpressResponse();
        Response.Activate();
        
        return ResponseBuilder;
    }

    //public AsyncServiceScope Scope { get; set; }
    public IServiceProvider Services { get; set; } = null!;
    
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
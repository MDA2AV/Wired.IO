using URocket.Connection;
using Wired.IO.Handlers.Http11Express.Request;
using Wired.IO.Handlers.Http11Express.Response;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Rocket.Context;

// This class cannot be sealed, might have super types
public class Http11RocketContext : IBaseContext<IExpressRequest, IExpressResponse>
{
    public Connection Connection { get; internal set; } = null!;

    public IExpressRequest Request { get; } = new Http11ExpressRequest()
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

    public IServiceProvider Services { get; set; } = null!;
    
    public CancellationToken CancellationToken { get; set; }
    
    public void Clear()
    {
        Connection = null!;
        Response?.Clear();
        Request.Clear();
    }
    
    public void Dispose()
    {
        Response?.Dispose();
        Request.Dispose();
    }
}
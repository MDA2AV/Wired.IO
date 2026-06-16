using URocket.Connection;
using Wired.IO.Handlers.Http11Overclocked.Request;
using Wired.IO.Handlers.Http11Overclocked.Response;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;

namespace Wired.IO.Handlers.Http11Overclocked.Context;

public class Http11OverclockedContext : IBaseContext<IBaseRequest, IOverclockedResponse>
{
    public Connection Connection { get; internal set; } = null!;
    
    public IBaseRequest Request { get; } = new Http11OverclockedRequest();

    public IOverclockedResponse? Response { get; private set; }
    
    private OverclockedResponseBuilder? _responseBuilder;
    
    private OverclockedResponseBuilder ResponseBuilder => _responseBuilder ??= new OverclockedResponseBuilder(Response!);
    
    public OverclockedResponseBuilder Respond()
    {
        Response ??= new Http11OverclockedResponse();
        Response.Activate();
        
        return ResponseBuilder;
    }

    public IServiceProvider Services { get; set; } = null!;
    
    public CancellationToken CancellationToken { get; set; }

    public void Clear()
    {
        Request.Clear();
        Response?.Clear();
    }
    
    public void Dispose() { }
}
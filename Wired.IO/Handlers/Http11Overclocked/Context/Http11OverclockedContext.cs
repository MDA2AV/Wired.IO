using URocket.Connection;
using Wired.IO.Handlers.Http11Overclocked.Request;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Handlers.Http11Overclocked.Context;

public class Http11OverclockedContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    public Connection Connection { get; internal set; } = null!;
    public IBaseRequest Request { get; } = new Http11OverclockedRequest();

    public IBaseResponse? Response { get; } = null!;

    public IServiceProvider Services { get; set; } = null!;
    
    public CancellationToken CancellationToken { get; set; }
    
    public void Clear() { }
    
    public void Dispose() { }
}
using Wired.IO.Protocol.Request;

namespace Wired.IO.Handlers.Http11Overclocked.Request;

public class Http11OverclockedRequest : IBaseRequest
{
    public string Route { get; set; } = null!;

    public string HttpMethod { get; set; } = null!;
    
    public void Clear() { }
    
    public void Dispose() { }
}
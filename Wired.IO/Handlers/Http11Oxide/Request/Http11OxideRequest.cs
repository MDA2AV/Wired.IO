using Wired.IO.Protocol.Request;

namespace Wired.IO.Handlers.Http11Oxide.Request;

public class Http11OxideRequest : IBaseRequest
{
    public string Route { get; set; } = null!;

    public string HttpMethod { get; set; } = null!;

    public void Clear()
    {
        
    }
    
    public void Dispose() { }
}
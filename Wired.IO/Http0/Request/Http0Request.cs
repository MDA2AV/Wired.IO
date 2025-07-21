using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Http0.Request;

public sealed class Http0Request : IRequest
{
    public string Route { get; set; }
    public string HttpMethod { get; set; }
    public PooledDictionary<string, ReadOnlyMemory<char>>? QueryParameters { get; set; }
    public PooledDictionary<string, string> Headers { get; set; } = null!;
    public ReadOnlyMemory<byte> Content { get; set; }
    public string ContentAsString { get; }
    public ConnectionType ConnectionType { get; set; }
    public void Clear()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Overclocked.Response;

public class Http11OverclockedResponse : IOverclockedResponse
{
    private bool _active;

    public void Activate() => _active = true;
    
    public bool IsActive() => _active;
    
    public ResponseStatus Status { get; set; } = ResponseStatus.Ok;

    public Action ContentHandler { get; set; } = null!;

    public Func<Task> AsyncContentHandler { get; set; } = null!;

    public Utf8View ContentType { get; set; }
    
    public ulong? ContentLength { get; set; }

    public void Clear()
    {
        _active = false;
        
        ContentType = default;
        ContentLength = null;
        
    }
    
    public void Dispose()
    {
    }
}
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Oxide.Response;

public interface IOxideResponse : IBaseResponse
{
    void Activate();

    bool IsActive();
    
    /// <summary>
    /// The type of the content.
    /// </summary>
    Utf8View ContentType { get; set; }
    
    ulong? ContentLength { get; set; }
    
    ResponseStatus Status { get; set; }
    
    Action ContentHandler { get; set; }
    
    Func<Task> AsyncContentHandler { get; set; }
    
    void Clear();
}
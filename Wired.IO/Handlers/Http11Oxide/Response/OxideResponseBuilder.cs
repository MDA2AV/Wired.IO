using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Oxide.Response;

public enum ContentStrategy
{
    Utf8JsonWriter
}

public class OxideResponseBuilder(IOxideResponse response)
{
    
    
    public OxideResponseBuilder Content(Action contentHandler, ulong? length = null)
    {
        response.ContentLength = length;
        response.ContentHandler = contentHandler;

        return this;
    }
    
    public OxideResponseBuilder Type(ReadOnlySpan<byte> contentType)
    {
        response.ContentType = Utf8View.FromLiteral(contentType);
        
        return this;
    }
    
    public OxideResponseBuilder Status(ResponseStatus status)
    {
        response.Status = status;
        
        return this;
    }
    
    public OxideResponseBuilder Length(ulong length)
    {
        response.ContentLength = length;
        
        return this;
    }
}
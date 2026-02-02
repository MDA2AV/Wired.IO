using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Overclocked.Response;

public enum ContentStrategy
{
    Utf8JsonWriter
}

public class OverclockedResponseBuilder(IOverclockedResponse response)
{
    
    
    public OverclockedResponseBuilder Content(Action contentHandler, ulong? length = null)
    {
        response.ContentLength = length;
        response.ContentHandler = contentHandler;

        return this;
    }
    
    public OverclockedResponseBuilder Type(ReadOnlySpan<byte> contentType)
    {
        response.ContentType = Utf8View.FromLiteral(contentType);
        
        return this;
    }
    
    public OverclockedResponseBuilder Status(ResponseStatus status)
    {
        response.Status = status;
        
        return this;
    }
    
    public OverclockedResponseBuilder Length(ulong length)
    {
        response.ContentLength = length;
        
        return this;
    }
}
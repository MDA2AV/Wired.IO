using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express;

public interface IExpressRequest : IBaseRequest
{
    /// <summary>
    /// Gets the query string parameters portion of the request URI, 
    /// typically represented as a URL-encoded string following the '?' in the URI.
    /// </summary>
    PooledDictionary<string, string>? QueryParameters { get; set; }

    /// <summary>
    /// Gets the collection of HTTP request headers as raw strings, typically in the format "HeaderName: HeaderValue".
    /// </summary>
    PooledDictionary<string, string> Headers { get; set; }

    /// <summary>
    /// Gets or sets the raw request body as a byte array.  
    /// May be <c>null</c> if the request does not contain a body.
    /// </summary>
    byte[]? Content { get; set; }

    /// <summary>
    /// Gets the request body decoded as a UTF-8 string.  
    /// Returns an empty string if <see cref="Content"/> is <c>null</c>.
    /// </summary>
    string ContentAsString { get; }

    /// <summary>
    /// Gets or sets the length of the request body in bytes.  
    /// Typically derived from the <c>Content-Length</c> header or from 
    /// the total size of chunked transfer-encoding data.
    /// </summary>
    int ContentLength { get; set; }
}
using Wired.IO.Utilities;

namespace Wired.IO.Protocol.Request;

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
}
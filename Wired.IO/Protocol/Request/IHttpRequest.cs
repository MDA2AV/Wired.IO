using Wired.IO.Utilities;

namespace Wired.IO.Protocol.Request;

/// <summary>
/// Represents a minimal abstraction of an HTTP request containing essential routing information.
/// </summary>
public interface IHttpRequest : IDisposable
{
    /// <summary>
    /// Gets the route or path portion of the HTTP request, typically used to determine the target endpoint.
    /// </summary>
    string Route { get; set; }

    /// <summary>
    /// Gets the HTTP method of the request (e.g., "GET", "POST", "PUT", "DELETE"), 
    /// which indicates the action to be performed on the resource.
    /// </summary>
    string HttpMethod { get; set; }

    /// <summary>
    /// Gets the query string parameters portion of the request URI, 
    /// typically represented as a URL-encoded string following the '?' in the URI.
    /// </summary>
    PooledDictionary<string, ReadOnlyMemory<char>>? QueryParameters { get; set; }

    /// <summary>
    /// Gets the collection of HTTP request headers as raw strings, typically in the format "HeaderName: HeaderValue".
    /// </summary>
    PooledDictionary<string, string> Headers { get; set; }

    /// <summary>
    /// Gets or sets the raw binary content (body) of the HTTP request, if present.
    /// </summary>
    /// <remarks>
    /// Commonly used for POST or PUT requests where the body carries data such as JSON, form values, or file uploads.
    /// </remarks>
    ReadOnlyMemory<byte> Content { get; set; }

    /// <summary>
    /// Gets the raw content of the request body interpreted as a UTF-8 string.
    /// </summary>
    /// <remarks>
    /// This is a convenience property for accessing textual request bodies. It may throw if the content is not valid UTF-8.
    /// </remarks>
    string ContentAsString { get; }

    /// <summary>
    /// Gets or sets the type of connection used for the request, such as Keep-Alive or Close.
    /// </summary>
    /// <remarks>
    /// Indicates whether the connection should persist after the response is sent (e.g., for HTTP/1.1 Keep-Alive support).
    /// </remarks>
    ConnectionType ConnectionType { get; set; }

    void Clear();
}
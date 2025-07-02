using System.Text;
using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Http11.Request;

/// <summary>
/// Represents an HTTP/1.1 request, including headers, body content, method, route, and connection metadata.
/// </summary>
/// <remarks>
/// This class implements <see cref="IHttpRequest"/> and serves as the concrete model
/// for parsing and accessing incoming request data in the HTTP/1.1 protocol.
/// </remarks>
public class Http11Request : IHttpRequest
{
    /// <summary>
    /// Gets or sets the collection of HTTP request headers.
    /// </summary>
    /// <remarks>
    /// Keys are header names (case-insensitive), and values are their corresponding string values.
    /// This collection is pooled for performance and reused across requests.
    /// </remarks>
    public PooledDictionary<string, string> Headers { get; set; } = null!;

    /// <summary>
    /// Gets or sets the raw request body content as a binary buffer.
    /// </summary>
    /// <remarks>
    /// This may represent data such as JSON, form data, or binary uploads.
    /// </remarks>
    public ReadOnlyMemory<byte> Content { get; set; }

    /// <summary>
    /// Gets the request body decoded as a UTF-8 string.
    /// </summary>
    /// <remarks>
    /// This is a convenience property for text-based payloads. If the content is not valid UTF-8, decoding errors may occur.
    /// </remarks>
    public string ContentAsString => Encoding.UTF8.GetString(Content.Span);

    /// <summary>
    /// Gets or sets the type of connection (e.g., Keep-Alive, Close) as determined by the request headers.
    /// </summary>
    public ConnectionType ConnectionType { get; set; }

    /// <summary>
    /// Gets or sets the query parameters parsed from the URI.
    /// </summary>
    /// <remarks>
    /// Each key is a parameter name, and the value is the decoded value as <see cref="ReadOnlyMemory{Char}"/>.
    /// This collection is pooled and cleared on disposal.
    /// </remarks>
    public PooledDictionary<string, ReadOnlyMemory<char>>? QueryParameters { get; set; } = null!;

    /// <summary>
    /// Gets or sets the route or path portion of the request URI.
    /// </summary>
    /// <example>/api/users</example>
    public string Route { get; set; } = null!;

    /// <summary>
    /// Gets or sets the HTTP method of the request (e.g., GET, POST).
    /// </summary>
    public string HttpMethod { get; set; } = null!;

    public void Clear()
    {
        Headers?.Clear();
        QueryParameters?.Clear();
    }

    private bool _disposed;

    /// <summary>
    /// Disposes the request, clearing pooled dictionaries and releasing resources.
    /// </summary>
    /// <remarks>
    /// Clears <see cref="Headers"/> and <see cref="QueryParameters"/> to allow reuse from the pool.
    /// Prevents multiple calls via internal guard flag.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}
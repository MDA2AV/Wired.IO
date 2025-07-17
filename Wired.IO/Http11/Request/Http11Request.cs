using System.Text;
using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Http11.Request;

/// <summary>
/// Represents an HTTP/1.1 request, including headers, body content, method, route, and connection metadata.
/// </summary>
/// <remarks>
/// This class implements <see cref="IRequest"/> and serves as the concrete model
/// for parsing and accessing incoming request data in the HTTP/1.1 protocol.
/// </remarks>
public class Http11Request : IRequest
{
    public PooledDictionary<string, string> Headers { get; set; } = null!;

    public ReadOnlyMemory<byte> Content { get; set; }

    public string ContentAsString => Encoding.UTF8.GetString(Content.Span);

    public ConnectionType ConnectionType { get; set; }

    public PooledDictionary<string, ReadOnlyMemory<char>>? QueryParameters { get; set; } = null!;

    public string Route { get; set; } = null!;

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
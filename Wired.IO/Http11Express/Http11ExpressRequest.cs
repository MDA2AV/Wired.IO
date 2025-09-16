using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express;

public class Http11ExpressRequest : IExpressRequest
{
    public string Route { get; set; } = null!;

    public string HttpMethod { get; set; } = null!;

    public PooledDictionary<string, string>? QueryParameters { get; set; } = null!;

    public PooledDictionary<string, string> Headers { get; set; } = null!;

    public ConnectionType ConnectionType { get; set; } = ConnectionType.KeepAlive;

    public void Clear()
    {
        Headers?.Clear();
        QueryParameters?.Clear();
    }

    private bool _disposed;

    /// <summary>
    /// Disposes the request, releasing resources.
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
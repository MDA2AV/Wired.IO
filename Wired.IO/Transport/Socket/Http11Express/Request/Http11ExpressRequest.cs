using System.Buffers;
using System.Runtime.CompilerServices;
using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Transport.Socket.Http11Express.Request;

[SkipLocalsInit]
public class Http11ExpressRequest : IExpressRequest
{
    public string Route { get; set; } = null!;

    public string HttpMethod { get; set; } = null!;

    public PooledDictionary<string, string>? QueryParameters { get; set; } = null!;

    public PooledDictionary<string, string> Headers { get; set; } = null!;

    public ConnectionType ConnectionType { get; set; } = ConnectionType.KeepAlive;

    public byte[]? Content { get; set; }

    public string ContentAsString => Encoders.Utf8Encoder.GetString(Content ?? [], 0, ContentLength);

    public int ContentLength { get; set; }

    public void Clear()
    {
        if (Content is not null)
        {
            ArrayPool<byte>.Shared.Return(Content, clearArray: false);
            Content = null;
        }

        ContentLength = 0;

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
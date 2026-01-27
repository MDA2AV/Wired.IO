using Wired.IO.Handlers.Http11Express.Response.Content;
using Wired.IO.Protocol.Response;
using Wired.IO.Protocol.Response.Headers;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Express.Response;

public class Http11ExpressResponse : IExpressResponse
{
    private bool _active;

    public void Activate() => _active = true;
    
    public bool IsActive() => _active;

    public PooledDictionary<Utf8View, Utf8View>? Utf8Headers { get; set; }

    private readonly ResponseHeaderCollection _headers = new();

    public ResponseStatus Status { get; set; } = ResponseStatus.Ok;

    public DateTime? Expires { get; set; }

    public DateTime? Modified { get; set; }

    public IEditableHeaderCollection Headers => _headers;

    public Utf8View ContentType { get; set; }

    public IExpressResponseContent? Content { get; set; }

    public Utf8View Utf8Content { get; set; }

    public ContentLengthStrategy ContentLengthStrategy { get; set; }

    public string? ContentEncoding { get; set; }

    public ulong? ContentLength { get; set; }

    public Action Handler { get; set; } = null!;
    
    public Func<Task> AsyncHandler { get; set; } = null!;

    public void Clear()
    {
        _active = false;

        ContentType = default;
        Utf8Content = default;

        ContentLength = null;
        ContentLengthStrategy = ContentLengthStrategy.None;

        Utf8Headers?.Clear();
        Headers.Clear();
    }

    #region IDisposable Support

    private bool _disposed;

    /// <summary>
    /// Disposes the response and its header collection.
    /// </summary>
    /// <remarks>
    /// This releases any pooled resources associated with the response headers.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }

    #endregion
}
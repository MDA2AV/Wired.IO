using Wired.IO.Http11.Response.Content;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Protocol.Response.Headers;

namespace Wired.IO.Http11.Response;

public class Http11Response : IResponse
{
    private readonly ResponseHeaderCollection _headers = new();

    public FlexibleResponseStatus Status { get; set; } = new FlexibleResponseStatus(ResponseStatus.Ok);

    public DateTime? Expires { get; set; }

    public DateTime? Modified { get; set; }

    public string? this[string field]
    {
        get => _headers.GetValueOrDefault(field);
        set
        {
            if (value is not null)
            {
                _headers[field] = value;
            }
            else
            {
                _headers.Remove(field);
            }
        }
    }

    public IEditableHeaderCollection Headers => _headers;

    public FlexibleContentType? ContentType { get; set; }

    public IResponseContent? Content { get; set; }

    public string? ContentEncoding { get; set; }

    public ulong? ContentLength { get; set; }

    public void Clear()
    {
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
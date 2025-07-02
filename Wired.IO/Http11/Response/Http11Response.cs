using Wired.IO.Http11.Response.Content;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Protocol.Response.Headers;

namespace Wired.IO.Http11.Response;

public class Http11Response : IResponse
{
    private readonly ResponseHeaderCollection _headers = new();

    /// <summary>
    /// Gets or sets the HTTP response status, including status code and reason phrase.
    /// </summary>
    public FlexibleResponseStatus Status { get; set; } = new FlexibleResponseStatus(ResponseStatus.Ok);

    /// <summary>
    /// Gets or sets the expiration timestamp for the response.
    /// </summary>
    /// <remarks>
    /// This value is written to the <c>Expires</c> header if set.
    /// </remarks>
    public DateTime? Expires { get; set; }

    /// <summary>
    /// Gets or sets the last modified timestamp for the response.
    /// </summary>
    /// <remarks>
    /// This value is written to the <c>Last-Modified</c> header if set.
    /// </remarks>
    public DateTime? Modified { get; set; }

    /// <summary>
    /// Gets or sets a response header by name.
    /// </summary>
    /// <param name="field">The name of the header field (case-insensitive).</param>
    /// <returns>The value of the header if present; otherwise, <c>null</c>.</returns>
    /// <remarks>
    /// - Setting a non-null value will insert or overwrite the header.
    /// - Setting a <c>null</c> value will remove the header if it exists.
    /// </remarks>
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

    /// <summary>
    /// Gets the editable collection of HTTP headers for this response.
    /// </summary>
    /// <remarks>
    /// This header collection is pooled and automatically cleared or disposed when the response is reused or disposed.
    /// </remarks>
    public IEditableHeaderCollection Headers => _headers;

    /// <summary>
    /// Gets or sets the response content type (e.g., <c>text/html</c>, <c>application/json</c>).
    /// </summary>
    public FlexibleContentType? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the response content body.
    /// </summary>
    /// <remarks>
    /// This content will be streamed to the client using either content-length or chunked transfer encoding.
    /// </remarks>
    public IResponseContent? Content { get; set; }

    /// <summary>
    /// Gets or sets the content encoding (e.g., <c>gzip</c>, <c>br</c>).
    /// </summary>
    /// <remarks>
    /// This value is written to the <c>Content-Encoding</c> header if set.
    /// </remarks>
    public string? ContentEncoding { get; set; }

    /// <summary>
    /// Gets or sets the content length of the response body in bytes.
    /// </summary>
    /// <remarks>
    /// If not set, the response is assumed to use chunked encoding.
    /// </remarks>
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
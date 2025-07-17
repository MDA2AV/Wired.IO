using Wired.IO.Http11.Response.Content;
using Wired.IO.Protocol.Response.Headers;

namespace Wired.IO.Protocol.Response;

/// <summary>
/// The response to be sent to the connected client for a given request.
/// </summary>
public interface IResponse : IDisposable
{

    #region Protocol

    /// <summary>
    /// The HTTP response code.
    /// </summary>
    FlexibleResponseStatus Status { get; set; }

    #endregion

    #region Headers

    /// <summary>
    /// Define, when this resource will expire.
    /// </summary>
    DateTime? Expires { get; set; }

    /// <summary>
    /// Define, when this resource has been changed the last time.
    /// </summary>
    DateTime? Modified { get; set; }

    /// <summary>
    /// Retrieve or set the value of a header field.
    /// </summary>
    /// <param name="field">The name of the header field</param>
    /// <returns>The value of the header field</returns>
    string? this[string field] { get; set; }

    /// <summary>
    /// The headers of the HTTP response.
    /// </summary>
    IEditableHeaderCollection Headers { get; }

    #endregion

    #region Content

    /// <summary>
    /// Gets or sets the response content body.
    /// </summary>
    /// <remarks>
    /// This content will be streamed to the client using either content-length or chunked transfer encoding.
    /// </remarks>
    IResponseContent? Content { get; set; }

    /// <summary>
    /// The type of the content.
    /// </summary>
    FlexibleContentType? ContentType { get; set; }

    /// <summary>
    /// The encoding of the content (e.g. "br").
    /// </summary>
    string? ContentEncoding { get; set; }

    /// <summary>
    /// The number of bytes the content consists of.
    /// </summary>
    ulong? ContentLength { get; set; }

    #endregion

    /// <summary>
    /// Clears the response state of the current context without disposing it.
    /// </summary>
    /// <remarks>
    /// This method is typically used to reset the context for reuse within a connection handling loop.
    /// </remarks>
    void Clear();

}
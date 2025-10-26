using Microsoft.Extensions.ObjectPool;
using System.IO.Pipelines;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Http11Express.Response.Content;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Protocol.Response.Headers;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express.Response;

public interface IExpressResponse : IBaseResponse
{
    void Activate();

    bool IsActive();
    
    #region Protocol

    /// <summary>
    /// The HTTP response code.
    /// </summary>
    ResponseStatus Status { get; set; }

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
    /// The headers of the HTTP response.
    /// </summary>
    IEditableHeaderCollection Headers { get; }

    PooledDictionary<Utf8View, Utf8View>? Utf8Headers { get; set; }

    #endregion

    #region Content

    /// <summary>
    /// Gets or sets the response content body.
    /// </summary>
    /// <remarks>
    /// This content will be streamed to the client using either content-length or chunked transfer encoding.
    /// </remarks>
    IExpressResponseContent? Content { get; set; }

    Utf8View Utf8Content { get; set; }

    /// <summary>
    /// The type of the content.
    /// </summary>
    Utf8View ContentType { get; set; }

    /// <summary>
    /// The encoding of the content (e.g. "br").
    /// </summary>
    string? ContentEncoding { get; set; }

    /// <summary>
    /// The number of bytes the content consists of.
    /// </summary>
    ulong? ContentLength { get; set; }

    ContentLengthStrategy ContentLengthStrategy { get; set; }

    #endregion

    /// <summary>
    /// Clears the response state of the current context without disposing it.
    /// </summary>
    /// <remarks>
    /// This method is typically used to reset the context for reuse within a connection handling loop.
    /// </remarks>
    void Clear();

    Action Handler { get; set; }
    
    Func<Task> AsyncHandler { get; set; }
}
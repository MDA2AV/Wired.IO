using Wired.IO.Http11.Response.Content;
using Wired.IO.Protocol.Response.Headers;

namespace Wired.IO.Protocol.Response;

/// <summary>
/// Allows to configure an HTTP response to be sent.
/// </summary>
public interface IResponseBuilder : IResponseModification<IResponseBuilder>
{
    /// <summary>
    /// Specifies the length of the content stream, if known.
    /// </summary>
    /// <param name="length">The length of the content stream</param>
    IResponseBuilder Length(ulong length);

    /// <summary>
    /// Specifies the content to be sent to the client.
    /// </summary>
    /// <param name="content">The content to be sent to the client</param>
    IResponseBuilder Content(IResponseContent content);
}
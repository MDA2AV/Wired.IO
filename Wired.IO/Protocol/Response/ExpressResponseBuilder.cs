using System.Runtime.CompilerServices;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Http11Express;
using Wired.IO.Utilities;

namespace Wired.IO.Protocol.Response;

public enum ContentLengthStrategy
{
    None,
    Known,
    KnownDirect,
    Chunked,
}

public class ExpressResponseBuilder(IExpressResponse response)
{
    public ExpressResponseBuilder Content(IExpressResponseContent content)
    {
        response.Content = content;
        response.ContentLength = content.Length ?? 0;

        response.ContentLengthStrategy = content.Length is not null
            ? ContentLengthStrategy.Known
            : ContentLengthStrategy.Chunked;

        return this;
    }

    public ExpressResponseBuilder Content(ReadOnlySpan<byte> content)
    {
        response.Utf8Content = Utf8View.FromLiteral(content);
        response.ContentLength = (ulong)content.Length;

        response.ContentLengthStrategy = ContentLengthStrategy.KnownDirect;

        return this;
    }

    public ExpressResponseBuilder Type(ReadOnlySpan<byte> contentType)
    {
        response.ContentType = Utf8View.FromLiteral(contentType);
        return this;
    }

    public ExpressResponseBuilder Status(ResponseStatus status)
    {
        response.Status = status;
        return this;
    }

    public ExpressResponseBuilder Header(string key, string value)
    {
        response.Headers.Add(key, value);
        return this;
    }

    public ExpressResponseBuilder Expires(DateTime expiryDate)
    {
        response.Expires = expiryDate;
        return this;
    }

    public ExpressResponseBuilder Modified(DateTime modificationDate)
    {
        response.Modified = modificationDate;
        return this;
    }

    public ExpressResponseBuilder Encoding(string encoding)
    {
        response.ContentEncoding = encoding;
        return this;
    }

    public ExpressResponseBuilder Length(ulong length)
    {
        response.ContentLength = length;
        return this;
    }
}
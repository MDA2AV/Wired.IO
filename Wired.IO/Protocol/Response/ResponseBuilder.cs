using Wired.IO.Http11.Response;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Http11Express;

namespace Wired.IO.Protocol.Response;

public class ResponseBuilder(IResponse response) : IResponseBuilder
{
    private readonly IResponse _response = response;

    public IResponseBuilder Content(IResponseContent content)
    {
        _response.Content = content;
        _response.ContentLength = content.Length;

        return this;
    }

    public IResponseBuilder Type(FlexibleContentType contentType)
    {
        _response.ContentType = contentType;
        return this;
    }

    public IResponseBuilder Type(string contentType)
    {
        _response.ContentType = new FlexibleContentType(contentType);
        return this;
    }

    public IResponseBuilder Status(ResponseStatus status)
    {
        _response.Status = new FlexibleResponseStatus(status);
        return this;
    }

    public IResponseBuilder Status(int status, string reason)
    {
        _response.Status = new FlexibleResponseStatus(status, reason);
        return this;
    }

    public IResponseBuilder Header(string key, string value)
    {
        _response.Headers.Add(key, value);
        return this;
    }

    public IResponseBuilder Expires(DateTime expiryDate)
    {
        _response.Expires = expiryDate;
        return this;
    }

    public IResponseBuilder Modified(DateTime modificationDate)
    {
        _response.Modified = modificationDate;
        return this;
    }

    public IResponseBuilder Encoding(string encoding)
    {
        _response.ContentEncoding = encoding;
        return this;
    }

    public IResponseBuilder Length(ulong length)
    {
        _response.ContentLength = length;
        return this;
    }
}
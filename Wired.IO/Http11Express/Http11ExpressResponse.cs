using Wired.IO.Http11.Response;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Protocol.Response.Headers;

namespace Wired.IO.Http11Express;

public class Http11ExpressResponse : IExpressResponse
{
    private bool _active;

    public void Activate() => _active = true;
    
    public bool IsActive() => _active;

    private readonly ResponseHeaderCollection _headers = new();

    public ResponseStatus Status { get; set; } = ResponseStatus.Ok;

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
        _active = false;
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

public class ExpressContentType
{
    
}

public class ExpressResponseBuilder(IExpressResponse response)
{
    private readonly IExpressResponse _response = response;

    public ExpressResponseBuilder Content(IResponseContent content)
    {
        _response.Content = content;
        _response.ContentLength = content.Length;

        return this;
    }

    public ExpressResponseBuilder Type(FlexibleContentType contentType)
    {
        _response.ContentType = contentType;
        return this;
    }

    public ExpressResponseBuilder Type(string contentType)
    {
        _response.ContentType = new FlexibleContentType(contentType);
        return this;
    }

    public ExpressResponseBuilder Status(ResponseStatus status)
    {
        _response.Status = status;
        return this;
    }

    public ExpressResponseBuilder Header(string key, string value)
    {
        _response.Headers.Add(key, value);
        return this;
    }

    public ExpressResponseBuilder Expires(DateTime expiryDate)
    {
        _response.Expires = expiryDate;
        return this;
    }

    public ExpressResponseBuilder Modified(DateTime modificationDate)
    {
        _response.Modified = modificationDate;
        return this;
    }

    public ExpressResponseBuilder Encoding(string encoding)
    {
        _response.ContentEncoding = encoding;
        return this;
    }

    public ExpressResponseBuilder Length(ulong length)
    {
        _response.ContentLength = length;
        return this;
    }
}

public enum ResponseStatus
{
    // 1xx
    Continue = 100,
    SwitchingProtocols = 101,
    Processing = 102,
    
    // 2xx
    Ok = 200,
    Created = 201,
    Accepted = 202,
    NoContent = 204,
    PartialContent = 206,
    MultiStatus = 207,
    AlreadyReported = 208,
    
    // 3xx
    MovedPermanently = 301,
    Found = 302,
    SeeOther = 303,
    NotModified = 304,
    TemporaryRedirect = 307,
    PermanentRedirect = 308,
    
    // 4xx
    BadRequest = 400,
    Unauthorized = 401,
    Forbidden = 403,
    NotFound = 404,
    MethodNotAllowed = 405,
    NotAcceptable = 406,
    ProxyAuthenticationRequired = 407,
    Conflict = 409,
    Gone = 410,
    LengthRequired = 411,
    PreconditionFailed = 412,
    RequestEntityTooLarge = 413,
    RequestUriTooLong = 414,
    UnsupportedMediaType = 415,
    RequestedRangeNotSatisfiable = 416,
    ExpectationFailed = 417,
    UnprocessableEntity = 422,
    Locked = 423,
    FailedDependency = 424,
    ReservedForWebDav = 425,
    UpgradeRequired = 426,
    PreconditionRequired = 428,
    TooManyRequests = 429,
    RequestHeaderFieldsTooLarge = 431,
    UnavailableForLegalReasons = 451,
    
    // 5xx
    InternalServerError = 500,
    NotImplemented = 501,
    BadGateway = 502,
    ServiceUnavailable = 503,
    GatewayTimeout = 504,
    HttpVersionNotSupported = 505,
    InsufficientStorage = 507,
    LoopDetected = 508,
    NotExtended = 510,
    NetworkAuthenticationRequired = 511
}

using Wired.IO.Http11Express;

namespace Wired.IO.Protocol.Response;

/// <summary>
/// The status of the response send to the client.
/// </summary>
public readonly struct FlexibleResponseStatus
{
    /// <summary>
    /// The known status, if any.
    /// </summary>
    public ResponseStatus? KnownStatus { get; }

    /// <summary>
    /// The raw HTTP status.
    /// </summary>
    public int RawStatus { get; }

    /// <summary>
    /// The reason phrase to be sent.
    /// </summary>
    public string Phrase { get; }

    private static readonly Dictionary<ResponseStatus, string> Mapping = new()
    {
        {
            ResponseStatus.Accepted, "Accepted"
        },
        {
            ResponseStatus.BadGateway, "Bad Gateway"
        },
        {
            ResponseStatus.BadRequest, "Bad Request"
        },
        {
            ResponseStatus.Created, "Created"
        },
        {
            ResponseStatus.Forbidden, "Forbidden"
        },
        {
            ResponseStatus.InternalServerError, "Internal Server Error"
        },
        {
            ResponseStatus.MethodNotAllowed, "Method Not Allowed"
        },
        {
            ResponseStatus.MovedPermanently, "Moved Permanently"
        },
        {
            ResponseStatus.Found, "Found"
        },
        {
            ResponseStatus.NoContent, "No Content"
        },
        {
            ResponseStatus.NotFound, "Not Found"
        },
        {
            ResponseStatus.NotImplemented, "Not Implemented"
        },
        {
            ResponseStatus.NotModified, "Not Modified"
        },
        {
            ResponseStatus.Ok, "OK"
        },
        {
            ResponseStatus.ServiceUnavailable, "Service Unavailable"
        },
        {
            ResponseStatus.Unauthorized, "Unauthorized"
        },
        {
            ResponseStatus.PartialContent, "Partial Content"
        },
        {
            ResponseStatus.MultiStatus, "Multi-Status"
        },
        {
            ResponseStatus.AlreadyReported, "Already Reported"
        },
        {
            ResponseStatus.SeeOther, "See Other"
        },
        {
            ResponseStatus.TemporaryRedirect, "Temporary Redirect"
        },
        {
            ResponseStatus.PermanentRedirect, "Permanent Redirect"
        },
        {
            ResponseStatus.Continue, "Continue"
        },
        {
            ResponseStatus.SwitchingProtocols, "Switching Protocols"
        },
        {
            ResponseStatus.NotAcceptable, "Not Acceptable"
        },
        {
            ResponseStatus.ProxyAuthenticationRequired, "Proxy Authentication Required"
        },
        {
            ResponseStatus.Conflict, "Conflict"
        },
        {
            ResponseStatus.Gone, "Gone"
        },
        {
            ResponseStatus.LengthRequired, "Length Required"
        },
        {
            ResponseStatus.PreconditionFailed, "Precondition Failed"
        },
        {
            ResponseStatus.RequestEntityTooLarge, "Request Entity Too Large"
        },
        {
            ResponseStatus.RequestUriTooLong, "Request Uri Too Long"
        },
        {
            ResponseStatus.UnsupportedMediaType, "Unsupported Media Type"
        },
        {
            ResponseStatus.RequestedRangeNotSatisfiable, "Requested Range Not Satisfiable"
        },
        {
            ResponseStatus.ExpectationFailed, "Expectation Failed"
        },
        {
            ResponseStatus.UnprocessableEntity, "Unprocessable Entity"
        },
        {
            ResponseStatus.Locked, "Locked"
        },
        {
            ResponseStatus.FailedDependency, "Failed Dependency"
        },
        {
            ResponseStatus.ReservedForWebDav, "Reserved For WebDAV"
        },
        {
            ResponseStatus.UpgradeRequired, "Upgrade Required"
        },
        {
            ResponseStatus.PreconditionRequired, "Precondition Required"
        },
        {
            ResponseStatus.TooManyRequests, "Too Many Requests"
        },
        {
            ResponseStatus.RequestHeaderFieldsTooLarge, "Request Header Fields Too Large"
        },
        {
            ResponseStatus.UnavailableForLegalReasons, "Unavailable For Legal Reasons"
        },
        {
            ResponseStatus.GatewayTimeout, "Gateway Timeout"
        },
        {
            ResponseStatus.HttpVersionNotSupported, "HTTP Version Not Supported"
        },
        {
            ResponseStatus.InsufficientStorage, "Insufficient Storage"
        },
        {
            ResponseStatus.LoopDetected, "Loop Detected"
        },
        {
            ResponseStatus.NotExtended, "Not Extended"
        },
        {
            ResponseStatus.NetworkAuthenticationRequired, "Network Authentication Required"
        },
        {
            ResponseStatus.Processing, "Processing"
        }
    };

    private static readonly Dictionary<int, ResponseStatus> CodeMapping =
        Mapping.Keys.ToDictionary(k => (int)k, k => k);

    public FlexibleResponseStatus(int status, string phrase)
    {
        RawStatus = status;
        Phrase = phrase;

        if (CodeMapping.TryGetValue(status, out var known))
        {
            KnownStatus = known;
        }
        else
        {
            KnownStatus = null;
        }
    }

    public FlexibleResponseStatus(ResponseStatus status)
    {
        KnownStatus = status;

        RawStatus = (int)status;
        Phrase = Mapping[status];
    }
}
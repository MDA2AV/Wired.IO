namespace Wired.IO.Protocol.Request;

public interface IBaseRequest : IDisposable
{
    /// <summary>
    /// Gets the route or path portion of the HTTP request, typically used to determine the target endpoint.
    /// </summary>
    string Route { get; set; }

    /// <summary>
    /// Gets the HTTP method of the request (e.g., "GET", "POST", "PUT", "DELETE"), 
    /// which indicates the action to be performed on the resource.
    /// </summary>
    string HttpMethod { get; set; }

    /// <summary>
    /// Gets or sets the raw binary content (body) of the HTTP request, if present.
    /// </summary>
    /// <remarks>
    /// Commonly used for POST or PUT requests where the body carries data such as JSON, form values, or file uploads.
    /// </remarks>
    ReadOnlyMemory<byte> Content { get; set; }

    /// <summary>
    /// Gets the raw content of the request body interpreted as a UTF-8 string.
    /// </summary>
    /// <remarks>
    /// This is a convenience property for accessing textual request bodies. It may throw if the content is not valid UTF-8.
    /// </remarks>
    string ContentAsString { get; }

    /// <summary>
    /// Gets or sets the type of connection used for the request, such as Keep-Alive or Close.
    /// </summary>
    /// <remarks>
    /// Indicates whether the connection should persist after the response is sent (e.g., for HTTP/1.1 Keep-Alive support).
    /// </remarks>
    ConnectionType ConnectionType { get; set; }

    /// <summary>
    /// Clears the request state of the current context without disposing it.
    /// </summary>
    /// <remarks>
    /// This method is typically used to reset the context for reuse within a connection handling loop.
    /// </remarks>
    void Clear();
}
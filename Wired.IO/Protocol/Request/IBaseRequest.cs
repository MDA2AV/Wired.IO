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
    /// Clears the request state of the current context without disposing it.
    /// </summary>
    /// <remarks>
    /// This method is typically used to reset the context for reuse within a connection handling loop.
    /// </remarks>
    void Clear();
}
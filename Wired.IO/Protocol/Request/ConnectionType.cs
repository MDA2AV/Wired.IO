namespace Wired.IO.Protocol.Request;

/// <summary>
/// Defines the types of HTTP connection behaviors supported by the web host.
/// </summary>
public enum ConnectionType
{
    /// <summary>
    /// The connection should be closed after the current request is processed.
    /// </summary>
    Close,

    /// <summary>
    /// The connection should be kept alive for potential subsequent requests.
    /// </summary>
    KeepAlive,

    /// <summary>
    /// The connection should be upgraded to a WebSocket connection.
    /// </summary>
    Websocket
}
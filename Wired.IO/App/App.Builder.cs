using System.Net.Security;
using System.Net;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Wired.IO.Builder;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

/// <summary>
/// Represents the core configuration and runtime state of a Wired.IO application instance.
/// </summary>
/// <typeparam name="TContext">The request context type implementing <see cref="IBaseContext{TRequest,TResponse}"/>.</typeparam>
public sealed partial class WiredApp<TContext> 
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the TLS configuration for the server.
    /// Defaults to <see cref="SslProtocols.None"/>, indicating no TLS enabled unless explicitly configured.
    /// </summary>
    public SslServerAuthenticationOptions SslServerAuthenticationOptions { get; set; } =
        new SslServerAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.None
        };

    /// <summary>
    /// Gets or sets the route map, grouped by HTTP method (e.g., GET, POST).
    /// Each entry maps an HTTP method to a set of encoded route patterns.
    /// </summary>
    public Dictionary<string, HashSet<string>> EncodedRoutes { get; set; } = new()
    {
        { HttpConstants.Get, [] },
        { HttpConstants.Post, [] },
        { HttpConstants.Put, [] },
        { HttpConstants.Delete, [] },
        { HttpConstants.Patch, [] },
        { HttpConstants.Head, [] },
        { HttpConstants.Options, [] },
        { HttpConstants.Trace, [] },
        { HttpConstants.Connect, [] },
        { "FlowControl", [] },
    };

    /// <summary>
    /// Partial routes, cannot have variables like /:id, the partial route must match exactly.
    /// </summary>
    public Dictionary<string, HashSet<string>> PartialExactMatchRoutes { get; set; } = new();
    /// <summary>
    /// Gets or sets the middleware pipeline.
    /// Each middleware is a delegate that processes the request context and calls the next middleware.
    /// </summary>
    public List<Func<TContext, Func<TContext, Task>, Task>> RootMiddleware { get; set; } = null!;

    /// <summary>
    /// Gets or sets the registered HTTP root endpoints.
    /// Each entry maps a composite route key to a handler function for the request context.
    /// </summary>
    public Dictionary<string, Func<TContext, Task>> RootEndpoints { get; set; } = null!;
    /// <summary>
    /// Gets or sets the registered HTTP grouped route endpoints.
    /// Each entry maps a composite route key to a handler function for the request context.
    /// </summary>
    public Dictionary<EndpointKey, Func<TContext, Task>> GroupEndpoints { get; set; } = null!;

    #endregion

    #region Internal Properties

    /// <summary>
    /// Gets or sets the local IP address the server will bind to.
    /// Defaults to 127.0.0.1.
    /// </summary>
    internal IPAddress IpAddress { get; set; } = IPAddress.Any;

    /// <summary>
    /// Gets or sets the port the server will listen on.
    /// Defaults to 9001.
    /// </summary>
    internal int Port { get; set; } = 9001;

    /// <summary>
    /// Gets or sets the maximum number of pending connections in the socket backlog.
    /// </summary>
    internal int Backlog { get; set; } = 16384;

    /// <summary>
    /// Gets or sets a value indicating whether TLS is enabled for this server instance.
    /// </summary>
    internal bool TlsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the logger factory used to create loggers for internal components.
    /// </summary>
    internal ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Gets or sets the main application logger.
    /// </summary>
    internal ILogger? Logger { get; set; }

    /// <summary>
    /// Endpoints can resolve dependencies from scoped provider
    /// </summary>
    internal bool ScopedEndpoints { get; set; } = true;

    /// <summary>
    /// If true: Use RootMiddleware and RootEndpoints, cannot create group routes.<br/>
    /// If false: Use GroupEndpoints, all user endpoints and middleware must be inside a group route.
    /// </summary>
    internal bool UseRootOnlyEndpoints { get; set; } = false;

    /// <summary>
    /// Gets or sets the HTTP handler responsible for dispatching requests and handling routing.
    /// </summary>
    public IHttpHandler<TContext> HttpHandler { get; set; } = null!;

    #endregion
}

/// <summary>
/// Contains constants representing the standard HTTP methods supported by Wired.IO.64
/// </summary>
public static class HttpConstants
{
    /// <summary>GET method - used to retrieve a resource.</summary>
    public const string Get = "GET";

    /// <summary>POST method - used to submit data to be processed.</summary>
    public const string Post = "POST";

    /// <summary>PUT method - used to replace a resource entirely.</summary>
    public const string Put = "PUT";

    /// <summary>DELETE method - used to delete a resource.</summary>
    public const string Delete = "DELETE";

    /// <summary>PATCH method - used to apply partial modifications to a resource.</summary>
    public const string Patch = "PATCH";

    /// <summary>HEAD method - used to retrieve headers for a resource, without the body.</summary>
    public const string Head = "HEAD";

    /// <summary>OPTIONS method - used to query communication options for a resource.</summary>
    public const string Options = "OPTIONS";

    /// <summary>TRACE method - used for diagnostic purposes to echo the received request.</summary>
    public const string Trace = "TRACE";

    /// <summary>CONNECT method - used to establish a tunnel to the server, typically for TLS.</summary>
    public const string Connect = "CONNECT";
}
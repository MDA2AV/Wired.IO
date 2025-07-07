using System.Net.Security;
using System.Net;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;

namespace Wired.IO.App;

public sealed partial class App<TContext> where TContext : IContext
{
    #region Public Properties

    public SslServerAuthenticationOptions SslServerAuthenticationOptions { get; set; } =
        new SslServerAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.None
        };

    public Dictionary<string, HashSet<string>> EncodedRoutes { get; set; } = new()
    {
        { HttpConstants.Get, [] },
        { HttpConstants.Post, [] },
        { HttpConstants.Put, [] },
        { HttpConstants.Delete, [] },
        { HttpConstants.Patch, [] },
        { HttpConstants.Head, [] },
        { HttpConstants.Options, [] },
    };

    public HashSet<string> Routes { get; set; } = [];

    public List<Func<TContext, Func<TContext, Task>, Task>> Middleware { get; set; } = null!;

    public Dictionary<string, Func<TContext, Task>> Endpoints { get; set; } = null!;

    #endregion

    #region Internal Properties

    internal IPAddress IpAddress { get; set; } = IPAddress.Parse("127.0.0.1");

    internal int Port { get; set; } = 9001;

    internal int Backlog { get; set; } = 8192;

    internal bool TlsEnabled { get; set; }

    internal ILoggerFactory? LoggerFactory { get; set; }

    internal ILogger? Logger { get; set; }

    internal IHttpHandler<TContext> HttpHandler { get; set; } = null!;

    #endregion
}

public static class HttpConstants
{
    public const string Get = "GET";
    public const string Post = "POST";
    public const string Put = "PUT";
    public const string Delete = "DELETE";
    public const string Patch = "PATCH";
    public const string Head = "HEAD";
    public const string Options = "OPTIONS";
}
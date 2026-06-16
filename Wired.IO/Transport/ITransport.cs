using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport;

public interface ITransport<TContext> : IDisposable
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    IPAddress IPAddress { get; set; }
    
    int Port { get; set; }
    
    int Backlog { get; set; }
    
    IHttpHandler HttpHandler { get; set; }
    
    ILogger? Logger { get; set; }
    
    bool TlsEnabled { get; set; }
    
    SslServerAuthenticationOptions SslServerAuthenticationOptions { get; set; }
    
    Func<TContext, Task> Pipeline { get; set; }
    
    Task ExecuteAsync(CancellationToken stoppingToken);
}
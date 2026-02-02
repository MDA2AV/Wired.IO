using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.NamedPipes;

public class NamedPipesTransport<TContext> : ITransport<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    public IPAddress IPAddress { get; set; } = null!;
    
    public int Port { get; set; }
    
    public int Backlog { get; set; }
    
    public IHttpHandler HttpHandler { get; set; } = null!;
    
    public ILogger? Logger { get; set; }
    
    public bool TlsEnabled { get; set; }
    
    public SslServerAuthenticationOptions SslServerAuthenticationOptions { get; set; } = null!;
    
    public Func<TContext, Task> Pipeline { get; set; } = null!;
    
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }
    
    public void Dispose()
    {
        // TODO release managed resources here
    }
}
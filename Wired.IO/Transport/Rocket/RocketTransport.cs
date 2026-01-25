using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using URocket.Engine;
using URocket.Engine.Configs;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.Rocket;

public class RocketTransport<TContext> : ITransport<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    private Engine _engine = null!;
    
    public IPAddress IPAddress { get; set; } = null!;
    
    public int Port { get; set; }
    
    public int Backlog { get; set; }

    public IHttpHandler HttpHandler { get; set; } = null!;

    public ILogger? Logger { get; set; }
    
    public bool TlsEnabled { get; set; } // Not Supported yet by uRocket

    public SslServerAuthenticationOptions SslServerAuthenticationOptions { get; set; } = null!; // Not Supported yet by uRocket
    
    public Func<TContext, Task> Pipeline { get; set; } = null!;

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CreateEngine();
        
        while(_engine.ServerRunning)
        {
            var connection = await _engine.AcceptAsync(stoppingToken);
            
            if(connection == null)
                continue;

            _ = ((IRocketHttpHandler<TContext>)HttpHandler).HandleClientAsync(connection, Pipeline, stoppingToken);
        }
    }

    private void CreateEngine()
    {
        _engine= new Engine(new EngineOptions
        {
            Port = (ushort)Port,
            Ip = "0.0.0.0",
            Backlog = Backlog,
            ReactorCount = 12
        });
        
        _engine.Listen();
    }

    public void Dispose()
    {
        _engine.Stop();
    }
}
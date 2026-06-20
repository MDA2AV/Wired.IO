using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using ioxide;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.Ioxide;

/// <summary>
/// The ioxide (io_uring) transport. Shared-nothing: one <see cref="Reactor"/> per core, each on its own
/// thread, each calling the tier handler per accepted connection. Reuses every other Wired layer (App,
/// Builder, DI, middleware, routing) unchanged — only the transport + parse core is ioxide-native.
/// </summary>
public sealed class IoxideTransport<TContext> : ITransport<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    public IPAddress IPAddress { get; set; } = null!;
    public int Port { get; set; }
    public int Backlog { get; set; }
    public IHttpHandler HttpHandler { get; set; } = null!;
    public ILogger? Logger { get; set; }
    public bool TlsEnabled { get; set; }                                          // kTLS via ioxide.tls — later slice
    public SslServerAuthenticationOptions SslServerAuthenticationOptions { get; set; } = null!;
    public Func<TContext, Task> Pipeline { get; set; } = null!;

    private Thread[] _threads = [];

    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reactors = Math.Min(Environment.ProcessorCount, 64);
        if (int.TryParse(Environment.GetEnvironmentVariable("IOXIDE_REACTORS"), out var r) && r > 0)
            reactors = r;

        var config = new ServerConfig
        {
            Port = (ushort)Port,
            ReactorCount = reactors,
            Incremental = false,
        };

        var handler = (IIoxideHttpHandler<TContext>)HttpHandler;
        var pipeline = Pipeline;

        _threads = new Thread[config.ReactorCount];
        for (var i = 0; i < config.ReactorCount; i++)
        {
            var reactor = new Reactor(i, config);
            reactor.Handle = (_, conn) => handler.HandleClientAsync(conn, pipeline, stoppingToken);
            _threads[i] = new Thread(reactor.Run) { Name = $"reactor-{i}", IsBackground = false };
            _threads[i].Start();
        }

        // The reactor threads carry the work; this just keeps ExecuteAsync alive until shutdown.
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public void Dispose() { }
}

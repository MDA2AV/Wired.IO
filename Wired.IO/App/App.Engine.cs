using Microsoft.Extensions.Hosting;
using System.Net.Sockets;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Wired.IO.App;

public sealed partial class App<TContext>
{
    internal App()
    {
        HostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService(_ => TlsEnabled
                    ? CreateTlsEnabledEngine()
                    : CreatePlainEngine());
            });
    }

    private Engine CreatePlainEngine() =>
        new Engine(async stoppingToken =>
        {
            CreateListeningSocket();
            await RunAcceptLoopAsync(HandlePlainClientAsync, stoppingToken);
        });

    private Engine CreateTlsEnabledEngine() =>
        new Engine(async stoppingToken =>
        {
            CreateListeningSocket();
            await RunAcceptLoopAsync(HandleTlsClientAsync, stoppingToken);
        });

    private Socket? _socket;

    private void CreateListeningSocket()
    {
        //IPv4
        //var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        //IPv6
        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        _socket.Bind(new IPEndPoint(IpAddress, Port));
        _socket.Listen(Backlog);
    }

    private async Task RunAcceptLoopAsync(Func<Socket, CancellationToken, Task> clientHandler, CancellationToken stoppingToken)
    {
        // Multiple concurrent accept loops for maximum throughput
        var acceptTasks = new Task[Environment.ProcessorCount];
        for (var i = 0; i < acceptTasks.Length; i++)
        {
            acceptTasks[i] = AcceptLoopAsync(clientHandler, stoppingToken);
        }

        await Task.WhenAll(acceptTasks);
    }

    private async Task AcceptLoopAsync(Func<Socket, CancellationToken, Task> clientHandler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _socket!.AcceptAsync(cancellationToken);

                client.NoDelay = true; // Disable Nagle's algorithm for low-latency communication

                // Handle client without blocking accept loop
                _ = HandleClientAsync(client, clientHandler, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Accept error: {ex.Message}");
                await Task.Delay(100, cancellationToken); // Brief delay on error
            }
        }
    }

    private async Task HandleClientAsync(Socket client, Func<Socket, CancellationToken, Task> clientHandler, CancellationToken stoppingToken)
    {
        try
        {
            await clientHandler(client, stoppingToken);
        }
        catch (Exception ex)
        {
            Logger?.LogTrace("Client could not be handled: {Exception}", ex);
        }
        finally
        {
            client.Dispose();
        }
    }

    private sealed class Engine(Func<CancellationToken, Task> action) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => action(stoppingToken);
    }
}
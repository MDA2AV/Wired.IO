using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Creates an <see cref="Engine"/> instance configured to accept and process plain (non-TLS) HTTP connections.
    /// </summary>
    /// <returns>An <see cref="Engine"/> that listens for unencrypted HTTP requests.</returns>
    private Engine CreatePlainEngine() =>
        new Engine(async stoppingToken =>
        {
            CreateListeningSocket();
            await RunAcceptLoopAsync(HandlePlainClientAsync, stoppingToken);
        });

    /// <summary>
    /// Creates an <see cref="Engine"/> instance configured to accept and process TLS-secured HTTP connections.
    /// </summary>
    /// <returns>An <see cref="Engine"/> that listens for HTTPS requests using TLS.</returns>
    private Engine CreateTlsEnabledEngine() =>
        new Engine(async stoppingToken =>
        {
            CreateListeningSocket();
            await RunAcceptLoopAsync(HandleTlsClientAsync, stoppingToken);
        });

    private Socket? _socket;

    /// <summary>
    /// Initializes the TCP listening socket and binds it to the configured IP address and port.
    /// Uses dual-stack IPv6 socket with IPv4 support enabled.
    /// </summary>
    private void CreateListeningSocket()
    {
        //IPv4
        //_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        ////IPv6
        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _socket.NoDelay = true;

        _socket.Bind(new IPEndPoint(IpAddress, Port));
        _socket.Listen(Backlog);
    }

    /// <summary>
    /// Launches parallel accept loops, one per processor core, to maximize concurrent connection handling throughput.
    /// </summary>
    /// <param name="clientHandler">A delegate that handles a connected <see cref="Socket"/>.</param>
    /// <param name="stoppingToken">A token used to cancel the accept loops.</param>
    /// <returns>A task that completes when all accept loops have terminated.</returns>
    private async Task RunAcceptLoopAsync(Func<Socket, CancellationToken, Task> clientHandler, CancellationToken stoppingToken)
    {
        // Multiple concurrent accept loops for maximum throughput
        //var acceptTasks = new Task[Environment.ProcessorCount/2];
        var acceptTasks = new Task[4];
        for (var i = 0; i < acceptTasks.Length; i++)
        {
            acceptTasks[i] = AcceptLoopAsync(clientHandler, stoppingToken);
        }

        await Task.WhenAll(acceptTasks);
    }

    /// <summary>
    /// Continuously accepts incoming connections and dispatches them to the provided client handler delegate.
    /// Each accepted client is processed in the background to avoid blocking the accept loop.
    /// </summary>
    /// <param name="clientHandler">A delegate that handles the accepted <see cref="Socket"/>.</param>
    /// <param name="cancellationToken">A token that cancels the accept loop when requested.</param>
    /// <returns>A task that completes when the loop is canceled or terminated due to error.</returns>
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

    /// <summary>
    /// Invokes the specified handler for a connected client socket, ensuring proper error logging and disposal.
    /// </summary>
    /// <param name="client">The connected <see cref="Socket"/> to handle.</param>
    /// <param name="clientHandler">The delegate responsible for processing the client connection.</param>
    /// <param name="stoppingToken">The token used to cancel the operation.</param>
    /// <returns>A task that completes when client handling is finished.</returns>
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
}
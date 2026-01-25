using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Wired.IO.MemoryBuffers;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.Socket;

/// <summary>
/// Encapsulates the server's execution logic and abstracts the engine behavior (e.g., plain or TLS).
/// </summary>
public sealed class SocketTransport<TContext> : ITransport<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    private System.Net.Sockets.Socket? _socket;
    
    public IPAddress IPAddress { get; set; } = null!;
    
    public int Port { get; set; }
    
    public int Backlog { get; set; }
    
    public IHttpHandler HttpHandler { get; set; } = null!;
    
    public ILogger? Logger { get; set; }
    
    public bool TlsEnabled { get; set; }
    
    public Func<TContext, Task> Pipeline { get; set; }

    /// <summary>
    /// Gets or sets the TLS configuration for the server.
    /// Defaults to <see cref="SslProtocols.None"/>, indicating no TLS enabled unless explicitly configured.
    /// </summary>
    public SslServerAuthenticationOptions SslServerAuthenticationOptions { get; set; } =
        new SslServerAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.None
        };

    public SocketTransport() { }

    /// <summary>
    /// Executes the engine logic using the provided cancellation token.
    /// </summary>
    /// <param name="stoppingToken">Token used to signal cancellation of the server loop.</param>
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CreateListeningSocket();
        if (TlsEnabled)
        {
            await RunAcceptLoopAsync(HandleTlsClientAsync, stoppingToken);
        }
        else
        {
            await RunAcceptLoopAsync(HandlePlainClientAsync, stoppingToken);
        }
    }
    
    /// <summary>
    /// Initializes the TCP listening socket and binds it to the configured IP address and port.
    /// Uses dual-stack IPv6 socket with IPv4 support enabled.
    /// </summary>
    private void CreateListeningSocket()
    {
        //IPv4
        //_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        //IPv6 DualStack
        _socket = new System.Net.Sockets.Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _socket.NoDelay = true;

        _socket.Bind(new IPEndPoint(IPAddress, Port));
        _socket.Listen(Backlog);
    }
    
    /// <summary>
    /// Launches parallel accept loops, one per processor core, to maximize concurrent connection handling throughput.
    /// </summary>
    /// <param name="clientHandler">A delegate that handles a connected <see cref="Socket"/>.</param>
    /// <param name="stoppingToken">A token used to cancel the accept loops.</param>
    /// <returns>A task that completes when all accept loops have terminated.</returns>
    private async Task RunAcceptLoopAsync(Func<System.Net.Sockets.Socket, CancellationToken, Task> clientHandler, CancellationToken stoppingToken)
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
    private async Task AcceptLoopAsync(Func<System.Net.Sockets.Socket, CancellationToken, Task> clientHandler, CancellationToken cancellationToken)
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
    private async Task HandleClientAsync(System.Net.Sockets.Socket client, Func<System.Net.Sockets.Socket, CancellationToken, Task> clientHandler, CancellationToken stoppingToken)
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
    
    // -------------------------------------
    // -------------------------------------
    
    /// <summary>
    /// Handles an incoming plain (non-TLS) TCP client connection.
    /// Wraps the client socket in a <see cref="PoolBufferedStream"/> and delegates request handling to <see cref="HttpHandler"/>.
    /// </summary>
    /// <param name="client">The connected TCP <see cref="Socket"/>.</param>
    /// <param name="stoppingToken">The <see cref="CancellationToken"/> used to cancel the operation.</param>
    private async Task HandlePlainClientAsync(System.Net.Sockets.Socket client, CancellationToken stoppingToken)
    {
        await using var networkStream = new PoolBufferedStream(new NetworkStream(client, ownsSocket: true), 65 * 1024);
        await ((ISocketHttpHandler<TContext>)HttpHandler).HandleClientAsync(
            client,
            networkStream, 
            Pipeline, 
            stoppingToken);
    }

    /// <summary>
    /// Handles an incoming TLS (SSL) client connection.
    /// Performs a TLS handshake using the configured <see cref="SslServerAuthenticationOptions"/> and delegates secure stream handling to <see cref="HttpHandler"/>.
    /// </summary>
    /// <param name="client">The connected TCP <see cref="Socket"/>.</param>
    /// <param name="stoppingToken">The <see cref="CancellationToken"/> used to cancel the operation.</param>
    /// <exception cref="SecurityException">
    /// Thrown if <see cref="SslServerAuthenticationOptions.ServerCertificate"/> is not set.
    /// </exception>
    private async Task HandleTlsClientAsync(System.Net.Sockets.Socket client, CancellationToken stoppingToken)
    {
        if (SslServerAuthenticationOptions.ServerCertificate is null)
        {
            throw new SecurityException("SecurityOptions.ServerCertificate is null");
        }

        // Create and configure the SSL stream for the client connection
        //
        // TODO: Investigate leaveInnerStreamOpen flag
        await using var stream = new PoolBufferedStream(new NetworkStream(client, ownsSocket: true), 65 * 1024);
        await using var sslStream = new SslStream(stream,
                                                  false,
                                                  SslServerAuthenticationOptions.RemoteCertificateValidationCallback);

        try
        {
            // Perform the TLS handshake
            //
            await sslStream.AuthenticateAsServerAsync(SslServerAuthenticationOptions, stoppingToken);
        }
        catch (Exception ex) when (HandleTlsException(ex))
        {
            // Unified handling of TLS failure message.
            //
            await SendTlsFailureMessageAsync(client);
        }

        // Handle the client connection securely
        //
        await ((ISocketHttpHandler<TContext>)HttpHandler).HandleClientAsync(
            client,
            sslStream,
            Pipeline,
            stoppingToken);
    }

    /// <summary>
    /// Handles and logs exceptions that occur during a TLS handshake.
    /// Always returns <c>true</c> to allow usage in a <c>catch when</c> filter.
    /// </summary>
    /// <param name="ex">The exception that was thrown during TLS negotiation.</param>
    /// <returns><c>true</c>, indicating the exception should be handled.</returns>
    private bool HandleTlsException(Exception ex)
    {
        switch (ex)
        {
            case AuthenticationException authEx:
                Logger?.LogTrace("TLS Handshake failed due to authentication error: {Message}", authEx.Message);
                break;
            case InvalidOperationException invalidOpEx:
                Logger?.LogTrace("TLS Handshake failed due to socket error: {Message}", invalidOpEx.Message);
                break;
            default:
                Logger?.LogTrace("Unexpected error during TLS Handshake: {Message}", ex.Message);
                break;
        }

        return true; // Ensure the exception is always caught
    }

    /// <summary>
    /// Sends a simple plaintext error message to the client if the TLS handshake fails,
    /// then closes the underlying socket.
    /// </summary>
    /// <param name="client">The TCP <see cref="Socket"/> to notify and close.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task SendTlsFailureMessageAsync(System.Net.Sockets.Socket client)
    {
        var messageBytes = "TLS Handshake failed. Closing connection."u8.ToArray();

        try
        {
            await client.SendAsync(messageBytes, SocketFlags.None);
        }
        catch
        {
            // Ignore errors while sending failure response
        }
        finally
        {
            client.Close();
        }
    }

    public void Dispose()
    {
        _socket?.Dispose();
    }
}
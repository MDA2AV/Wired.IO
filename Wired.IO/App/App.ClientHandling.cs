using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using Wired.IO.MemoryBuffers;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Handles an incoming plain (non-TLS) TCP client connection.
    /// Wraps the client socket in a <see cref="PoolBufferedStream"/> and delegates request handling to <see cref="HttpHandler"/>.
    /// </summary>
    /// <param name="client">The connected TCP <see cref="Socket"/>.</param>
    /// <param name="stoppingToken">The <see cref="CancellationToken"/> used to cancel the operation.</param>
    private async Task HandlePlainClientAsync(Socket client, CancellationToken stoppingToken)
    {
        await using var stream = new PoolBufferedStream(new NetworkStream(client, ownsSocket: true), 65 * 1024);
        await HttpHandler.HandleClientAsync(stream, Pipeline2, stoppingToken);
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
    private async Task HandleTlsClientAsync(Socket client, CancellationToken stoppingToken)
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
        await HttpHandler.HandleClientAsync(
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
    private static async Task SendTlsFailureMessageAsync(Socket client)
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
}
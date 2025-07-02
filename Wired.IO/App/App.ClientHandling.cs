using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security;
using Microsoft.Extensions.Logging;
using Wired.IO.MemoryBuffers;

namespace Wired.IO.App;

public sealed partial class App<TContext>
{
    private async Task HandlePlainClientAsync(Socket client, CancellationToken stoppingToken)
    {
        await using var stream = new PoolBufferedStream(new NetworkStream(client, ownsSocket: true), 65 * 1024);
        await HttpHandler.HandleClientAsync(stream, PipelineNoResponse, stoppingToken);
    }

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
            PipelineNoResponse,
            stoppingToken);
    }

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
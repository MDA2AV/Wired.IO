using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.Transport;

namespace Wired.IO.App;

/// <summary>
/// Represents a configured and runnable instance of a Wired.IO application.
/// Manages the lifecycle of the server including startup, shutdown, and resource disposal.
/// </summary>
/// <typeparam name="TContext">The request context type that implements <see cref="IBaseContext{TRequest,TResponse}"/>.</typeparam>
public sealed partial class WiredApp<TContext> : IDisposable
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Gets or sets the service provider for resolving dependencies at runtime.
    /// </summary>
    public IServiceProvider Services { get; set; } = null!;

    /// <summary>
    /// Gets or sets the service collection used to configure application dependencies before building the provider.
    /// </summary>
    public IServiceCollection ServiceCollection { get; set; } = new ServiceCollection();
    
    /// <summary>
    /// Underlying Engine Ongoing Task
    /// </summary>
    public Task? TransportTask => _transportTask;
    
    internal ITransport<TContext> Transport { get; set; } = null!;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly TaskCompletionSource<bool> _startedTcs = new();
    private bool _disposed;
    private Task? _transportTask;

    /// <summary>
    /// Asynchronously starts the application and begins listening for incoming requests.
    /// </summary>
    /// <returns>The same instance of <see cref="WiredApp{TContext}"/> once started.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the application has already been disposed.</exception>
    public async Task<WiredApp<TContext>> StartAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WiredApp<TContext>));
        
        Transport.IPAddress = IpAddress;
        Transport.Port = Port;
        Transport.Backlog = Backlog;
        Transport.TlsEnabled = TlsEnabled;
        Transport.Logger = Logger;
        Transport.SslServerAuthenticationOptions = SslServerAuthenticationOptions;
        _transportTask = Transport.ExecuteAsync(_cancellationTokenSource.Token);

        // Give the engine a moment to start listening
        await Task.Delay(10);
        _startedTcs.SetResult(true);

        return this;
    }

    /// <summary>
    /// Asynchronously starts the server (if not already started) and waits for it to terminate.
    /// </summary>
    /// <returns>A task that completes when the server stops.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the application has already been disposed.</exception>
    public async Task RunAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WiredApp<TContext>));

        if (_transportTask == null)
            await StartAsync();

        await _transportTask!;
    }

    /// <summary>
    /// Synchronously starts the application and begins listening for incoming requests.
    /// </summary>
    /// <returns>The current instance of <see cref="WiredApp{TContext}"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the application has already been disposed.</exception>
    public WiredApp<TContext> Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WiredApp<TContext>));
        
        Transport.IPAddress = IpAddress;
        Transport.Port = Port;
        Transport.Backlog = Backlog;
        Transport.TlsEnabled = TlsEnabled;
        Transport.Logger = Logger;
        Transport.SslServerAuthenticationOptions = SslServerAuthenticationOptions;
        _transportTask = Transport.ExecuteAsync(_cancellationTokenSource.Token);

        // Brief delay to ensure listening starts
        Thread.Sleep(10);
        _startedTcs.SetResult(true);

        return this;
    }

    /// <summary>
    /// Synchronously runs the application (blocking call).
    /// If not started, it will call <see cref="Start"/> internally.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the application has already been disposed.</exception>
    public void Run()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WiredApp<TContext>));

        if (_transportTask == null)
            Start();

        _transportTask!.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously stops the application and disposes the engine task if running.
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed)
            return;

        await _cancellationTokenSource.CancelAsync();

        if (_transportTask != null)
        {
            try
            {
                await _transportTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }
    }

    /// <summary>
    /// Releases all resources used by the application.
    /// Closes sockets, cancels any running operations, and disposes the service provider if necessary.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        
        Transport.Dispose();
        //_socket?.Dispose();

        if (Services is IDisposable disposableProvider)
            disposableProvider.Dispose();
    }
}

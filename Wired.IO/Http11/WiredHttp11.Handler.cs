using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.Collections;
using System.IO.Pipelines;
using System.Text;
using Wired.IO.Http11.Request;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol;
using Wired.IO.Utilities;
using System.Reflection;

namespace Wired.IO.Http11;

public partial class WiredHttp11<TContext>(IHandlerArgs args) : IHttpHandler<TContext>
    where TContext : class, IContext, new()
{

    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new DefaultPooledObjectPolicy<TContext>(), 8192);

    /// <summary>
    /// Handles a client connection by processing incoming HTTP/1.1 requests and executing the middleware pipeline.
    /// </summary>
    /// <param name="stream">The network stream for communication with the client.</param>
    /// <param name="pipeline"></param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> to signal when the operation should stop.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method implements the core HTTP/1.1 request processing logic:
    /// 
    /// 1. Connection Lifecycle:
    ///    - Reads the initial headers from the client request
    ///    - Processes requests in a loop for persistent connections (keep-alive)
    ///    - Closes the connection based on connection header or after processing a non-keep-alive request
    ///    
    /// 2. Request Processing:
    ///    - Parses HTTP headers to extract the route, HTTP method, and other metadata
    ///    - Handles static file requests if the route points to a file and static file serving is enabled
    ///    - For non-file requests, extracts the request body and creates an Http11Request object
    ///    - Creates a scoped service provider for dependency injection
    ///    - Executes the middleware pipeline against the request context
    ///    
    /// 3. Special Cases:
    ///    - Handles WebSocket connection upgrades
    ///    - Validates request format and throws exceptions for invalid requests
    ///    
    /// The method supports HTTP/1.1 features including persistent connections, allowing
    /// multiple requests to be processed over the same TCP connection for improved performance.
    /// </remarks>
#if NET9_0_OR_GREATER

    public async Task HandleClientAsync(Stream stream, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        var context = ContextPool.Get();
        context.Request = new Http11Request();

        context.Reader = PipeReader.Create(stream,
            new StreamPipeReaderOptions(MemoryPool<byte>.Shared, leaveOpen: true, bufferSize: 8192));

        context.Writer = PipeWriter.Create(stream,
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: true));

        context.Request.Headers = new PooledDictionary<string, string>(capacity: 16, comparer: StringComparer.OrdinalIgnoreCase);
        context.Request.QueryParameters = new PooledDictionary<string, ReadOnlyMemory<char>>();

        try
        {
            while (await ExtractHeadersAsync(context, stoppingToken))
            {
                // Determine the connection type based on headers
                context.Request.ConnectionType =
                    context.Request.Headers.TryGetValue("Connection", out var connectionValue)
                        ? GetConnectionType(connectionValue)
                        : ConnectionType.KeepAlive;

                // Handle WebSocket upgrade requests
                if (context.Request.ConnectionType is ConnectionType.Websocket)
                    await SendHandshakeResponse(context, ToRawHeaderString(context.Request.Headers));

                // Extract the URI and HTTP method from the request line
                IEnumerator enumerator = context.Request.Headers.GetEnumerator();
                enumerator.MoveNext();

                ParseHttpRequestLine(
                    ((KeyValuePair<string, string>)enumerator.Current).Value,
                    context.Request);

                // Check if the request is for a static file
                if (UseResources & IsRouteFile(context.Request.Route))
                    await FlushResource(context.Writer, context.Request.Route, stoppingToken);
                else
                    await pipeline(context);

                context.Clear();

                // For non keep-alive connections, break the loop
                if (context.Request.ConnectionType is not ConnectionType.KeepAlive &&
                    !stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            //Console.WriteLine("Operation was cancelled");
        }
        catch (IOException)
        {
            // Client disconnected - this is normal for HTTP connections
            //Console.WriteLine("Client connection closed");
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"Unexpected error in HandleClientAsync: {ex.Message}");
        }
        finally
        {
            context.Dispose();

            ContextPool.Return(context);
        }
    }

#endif

#if NET8_0

    public async Task HandleClientAsync(Stream stream, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        var context = ContextPool.Get();
        context.Request = new Http11Request();

        context.Reader = PipeReader.Create(stream,
            new StreamPipeReaderOptions(MemoryPool<byte>.Shared, leaveOpen: true, bufferSize: 8192));

        context.Writer = PipeWriter.Create(stream, 
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: true));

        context.Request.Headers = new PooledDictionary<string, string>(capacity: 16, comparer: StringComparer.OrdinalIgnoreCase);
        context.Request.QueryParameters = new PooledDictionary<string, ReadOnlyMemory<char>>();

        try
        {
            while (await ExtractHeadersAsync(context, stoppingToken))
            {
                // Determine the connection type based on headers
                context.Request.ConnectionType =
                    context.Request.Headers.TryGetValue("Connection", out var connectionValue)
                        ? GetConnectionType(connectionValue)
                        : ConnectionType.KeepAlive;

                // Handle WebSocket upgrade requests
                if (context.Request.ConnectionType == ConnectionType.Websocket)
                    await SendHandshakeResponse(context, ToRawHeaderString(context.Request.Headers));

                // Extract the URI and HTTP method from the request line
                IEnumerator enumerator = context.Request.Headers.GetEnumerator();
                enumerator.MoveNext();

                ParseHttpRequestLine(
                    ((KeyValuePair<string, string>)enumerator.Current).Value,
                    context.Request);

                // Check if the request is for a static file
                if (UseResources & IsRouteFile(context.Request.Route))
                    await FlushResource(context.Writer, context.Request.Route, stoppingToken);
                else
                    await pipeline(context);

                context.Clear();

                // For non keep-alive connections, break the loop
                if (context.Request.ConnectionType is not ConnectionType.KeepAlive &&
                    !stoppingToken.IsCancellationRequested)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            //Console.WriteLine("Operation was cancelled");
        }
        catch (IOException)
        {
            // Client disconnected - this is normal for HTTP connections
            //Console.WriteLine("Client connection closed");
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"Unexpected error in HandleClientAsync: {ex.Message}");
        }
        finally
        {
            context.Dispose();

            ContextPool.Return(context);
        }
    }

#endif

}
public record Http11HandlerArgs(
    bool UseResources,
    string ResourcesPath,
    Assembly ResourcesAssembly) : IHandlerArgs;
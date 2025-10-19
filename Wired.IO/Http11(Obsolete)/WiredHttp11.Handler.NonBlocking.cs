using System.Buffers;
using System.Collections;
using System.IO.Pipelines;
using Wired.IO.Protocol.Request;

namespace Wired.IO.Http11;

public partial class WiredHttp11<TContext>
{
    /// <summary>
    /// Handles an HTTP/1.1 connection using a non-blocking strategy, allowing pipelined
    /// requests to be processed concurrently over the same TCP connection.
    /// </summary>
    /// <param name="stream">The network stream representing the client connection.</param>
    /// <param name="pipeline">The request-handling delegate to invoke per request.</param>
    private async Task HandleNonBlocking(Stream stream, Func<TContext, Task> pipeline)
    {
        // Create a reusable PipeReader and PipeWriter wrapping the stream
        var reader = PipeReader.Create(stream,
            new StreamPipeReaderOptions(MemoryPool<byte>.Shared, leaveOpen: false, bufferSize: 8192));

        var writer = PipeWriter.Create(stream,
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: false));

        var cts = new CancellationTokenSource();
        var keepAlive = true;

        try
        {
            // Continue handling pipelined requests until connection is closed
            while (keepAlive)
            {
                // Rent a context instance from the pool
                var context = ContextPool.Get();
                context.CancellationToken = cts.Token;
                context.Reader = reader;
                context.Writer = writer;

                // Parse incoming headers and determine whether a request was received
                keepAlive = await ExtractHeadersAsync(context);

                // Parse the "Connection" header to decide connection behavior
                var connType =
                    context.Request.ConnectionType =
                        context.Request.Headers.TryGetValue("Connection", out var connectionValue)
                            ? GetConnectionType(connectionValue)
                            : ConnectionType.KeepAlive;

                // Dispatch request to pipeline without awaiting (enables pipelining)
                _ = ProcessPipelineRequest(context, pipeline);

                // If the client requested connection close, exit the loop
                if (connType is not ConnectionType.KeepAlive)
                    break;
            }
        }
        catch
        {
            // Cancel any in-progress requests upon fatal error
            await cts.CancelAsync();
        }
        finally
        {
            // Gracefully shut down the pipe
            await reader.CompleteAsync();
            await writer.CompleteAsync();
        }
    }

    /// <summary>
    /// Processes a single HTTP/1.1 request, handling WebSocket upgrades, static resources,
    /// or invoking the user-defined middleware pipeline.
    /// </summary>
    /// <param name="context">The context representing the current HTTP request and response state.</param>
    /// <param name="pipeline">The request-handling delegate to invoke for non-static routes.</param>
    public async Task ProcessPipelineRequest(TContext context, Func<TContext, Task> pipeline)
    {
        // Handle WebSocket upgrade if requested
        if (context.Request.ConnectionType is ConnectionType.Websocket)
        {
            await SendHandshakeResponse(context, ToRawHeaderString(context.Request.Headers));
        }

        // Parse the first header line to extract the HTTP request line (e.g. GET /index HTTP/1.1)
        IEnumerator enumerator = context.Request.Headers.GetEnumerator();
        enumerator.MoveNext();

        ParseHttpRequestLine(
            ((KeyValuePair<string, string>)enumerator.Current).Value,
            context.Request);

        // Check if the route maps to a known static resource
        if (UseResources & IsRouteFile(context.Request.Route))
        {
            await FlushResource(context.Writer, context.Request.Route, context.CancellationToken);
        }
        else
        {
            // Invoke the application pipeline to handle the request
            await pipeline(context);
        }

        // Return the context to the pool for reuse
        ContextPool.Return(context);
    }
}

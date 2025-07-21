using System.Buffers;
using System.Collections;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.Http11.Request;
using Wired.IO.Protocol.Request;

namespace Wired.IO.Http11;

public partial class WiredHttp11<TContext, TRequest>
{
    /// <summary>
    /// Handles an HTTP/1.1 connection using a blocking model — each request is processed sequentially
    /// before reading the next from the same TCP stream.
    /// </summary>
    /// <param name="stream">The network stream representing the client connection.</param>
    /// <param name="pipeline">The request processing pipeline to invoke for each parsed request.</param>
    private async Task HandleBlocking(Stream stream, Func<TContext, Task> pipeline)
    {
        // Rent a context object from the pool
        var context = ContextPool.Get();

        // Assign a new CancellationTokenSource to support per-request cancellation
        var cts = new CancellationTokenSource();
        context.CancellationToken = cts.Token;

        // Wrap the stream in a PipeReader and PipeWriter for efficient buffered reads/writes
        context.Reader = PipeReader.Create(stream,
            new StreamPipeReaderOptions(MemoryPool<byte>.Shared, leaveOpen: true, bufferSize: 8192));

        context.Writer = PipeWriter.Create(stream,
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: true));

        try
        {
            // Loop to handle multiple requests on the same connection (keep-alive)
            while (await ExtractHeadersAsync(context))
            {
                var request = Unsafe.As<Http11Request>(context.Request);

                // Determine connection type: keep-alive, close, or websocket
                request.ConnectionType =
                    request.Headers.TryGetValue("Connection", out var connectionValue)
                        ? GetConnectionType(connectionValue)
                        : ConnectionType.KeepAlive;

                // If WebSocket upgrade requested, perform handshake and skip HTTP handling
                if (context.Request.ConnectionType is ConnectionType.Websocket)
                    await SendHandshakeResponse(context, ToRawHeaderString(request.Headers));

                // Parse the HTTP request line (e.g. "GET /index.html HTTP/1.1") from the first header line
                IEnumerator enumerator = request.Headers.GetEnumerator();
                enumerator.MoveNext();

                ParseHttpRequestLine(
                    ((KeyValuePair<string, string>)enumerator.Current).Value,
                    context.Request);

                // Check if the requested route maps to a static embedded resource
                if (UseResources & IsRouteFile(context.Request.Route))
                {
                    await FlushResource(context.Writer, context.Request.Route, context.CancellationToken);
                }
                else
                {
                    // Invoke user-defined middleware pipeline
                    await pipeline(context);
                }

                // Clear context state for reuse in the next request (on the same socket)
                context.Clear();

                // If the client indicated "Connection: close", exit the loop
                if (context.Request.ConnectionType is not ConnectionType.KeepAlive)
                    break;
            }
        }
        catch
        {
            // Swallow all exceptions; connection will be closed silently
            await cts.CancelAsync();
        }
        finally
        {
            // Gracefully complete the reader/writer to release underlying resources
            await context.Reader.CompleteAsync();
            await context.Writer.CompleteAsync();

            // Return context to pool for reuse
            ContextPool.Return(context);
        }
    }
}
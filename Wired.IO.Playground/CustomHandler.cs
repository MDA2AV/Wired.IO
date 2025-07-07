using System.Buffers;
using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;

namespace Wired.IO.Playground;

public class CustomContext : IContext
{
    public void Dispose()
    {
        // TODO release managed resources here
    }

    public PipeReader Reader { get; set; }
    public PipeWriter Writer { get; set; }
    public IRequest Request { get; set; }
    public AsyncServiceScope Scope { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public IResponse? Response { get; set; }
    public void Clear()
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<IWiredEvent> WiredEvents { get; }
    public void AddWiredEvent(IWiredEvent wiredEvent)
    {
        throw new NotImplementedException();
    }

    public void ClearWiredEvents()
    {
        throw new NotImplementedException();
    }
}

public class CustomHttpHandler<TContext> : IHttpHandler<TContext>
    where TContext : class, IContext, new()
{
    // Pool context objects for less memory pressure (optional)
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new DefaultPooledObjectPolicy<TContext>(), 8192);

    public async Task HandleClientAsync(Stream stream, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        // Get a context object from pool (or create a new instance if not pooling)
        var context = ContextPool.Get();

        // Create a new IRequest or use pooling
        //context.Request = new CustomRequest();

        // Set up the PipeReader and PipeWriter for the context.
        // You can also skip this and use the Stream directly to read and write from socket.
        // However, the preferred way is to use PipeReader and PipeWriter for better performance.
        // Also, if you decide to use Stream, you will need to cast the IContext passed to the endpoint
        // since IContext does not expose the Stream directly.
        context.Reader = PipeReader.Create(stream,
            new StreamPipeReaderOptions(MemoryPool<byte>.Shared, leaveOpen: true, bufferSize: 8192));

        context.Writer = PipeWriter.Create(stream,
            new StreamPipeWriterOptions(MemoryPool<byte>.Shared, leaveOpen: true));



        // The next section is typically wrapped in a loop or equivalent if the connection is persistent (keep-alive).

        // Read the received request headers and set the context's HttpMethod and Route

        // Call the pipeline callback, it will trigger the middleware pipeline and the endpoint

        // Handle Keep-Alive connections

        // Make sure to dispose managed resources and return the context to the pool


    }
}
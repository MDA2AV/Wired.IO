using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Playground;

public class CustomContext : IContext
{
    public PipeReader Reader { get; set; } = null!;
    public PipeWriter Writer { get; set; } = null!;

    public IHttpRequest Request { get; set; } = null!;
    public IResponse? Response { get; set; }

    public AsyncServiceScope Scope { get; set; }
    public CancellationToken CancellationToken { get; set; }

    public void Clear()
    {
        // Clear Resources
    }
    public void Dispose()
    {
        // Release managed resources here
    }
}

public class CustomRequest : IHttpRequest
{

    public string Route { get; set; }
    public string HttpMethod { get; set; }
    public PooledDictionary<string, ReadOnlyMemory<char>>? QueryParameters { get; set; }
    public PooledDictionary<string, string> Headers { get; set; }
    public ReadOnlyMemory<byte> Content { get; set; }
    public string ContentAsString { get; }
    public ConnectionType ConnectionType { get; set; }

    public void Clear()
    {
        // Clear Resources
    }
    public void Dispose()
    {
        // Release managed resources here
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

        // Create a new IHttpRequest or use pooling
        //context.Request = ...
    }
}
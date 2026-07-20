using ioxide;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.Oxide;

/// <summary>
/// Handler contract for the ioxide transport: driven once per accepted ioxide <see cref="Connection"/>,
/// it parses requests (Glyph11) and runs Wired's <paramref name="pipeline"/> (middleware + routing +
/// endpoint) over the tier's context.
/// </summary>
public interface IOxideHttpHandler<out TContext> : IHttpHandler
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    Task HandleClientAsync(
        Connection connection,
        Func<TContext, Task> pipeline,
        CancellationToken stoppingToken);
}

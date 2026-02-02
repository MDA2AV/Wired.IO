using URocket.Connection;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.Rocket;

public interface IRocketHttpHandler<out TContext> : IHttpHandler
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    Task HandleClientAsync(
        Connection connection,
        Func<TContext, Task> pipeline,
        CancellationToken stoppingToken);
}
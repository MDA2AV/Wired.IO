using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Transport.NamedPipes;

public interface INamedPipesHttpHandler<out TContext> : IHttpHandler
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    
}
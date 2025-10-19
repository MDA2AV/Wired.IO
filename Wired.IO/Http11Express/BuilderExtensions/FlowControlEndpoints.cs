using Wired.IO.Builder;
using Wired.IO.Http11Express.Context;
using Wired.IO.Http11Express.StaticHandlers;

namespace Wired.IO.Http11Express.BuilderExtensions;

public static class FlowControlEndpoints
{
    public static Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> AddNotFoundEndpoint(
        this Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> builder)
    {
        builder.MapFlowControl("NotFound", FlowControl.CreateEndpointNotFoundHandler<Http11ExpressContext>());

        return builder;
    }
}
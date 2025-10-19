using Wired.IO.App;
using Wired.IO.Builder;
using Wired.IO.Http11Express.Context;
using Wired.IO.Http11Express.StaticHandlers;

namespace Wired.IO.Http11Express.BuilderExtensions;

public static class StaticFiles
{
    public static Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> ServeStaticFilesExpress(
        this Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> builder, 
        string baseRoute, 
        Location location)
    {
        builder.ServeStaticFiles(baseRoute, location);
        builder.MapGet("/serve-static-resource", StaticResources.CreateStaticResourceHandler<Http11ExpressContext>());

        return builder;
    }

    public static Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> ServeSpaFilesExpress(
        this Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> builder,
        string baseRoute,
        Location location)
    {
        builder.ServeSpaFiles(baseRoute, location);
        builder.MapGet("/serve-spa-resource", StaticResources.CreateSpaResourceHandler<Http11ExpressContext>());

        return builder;
    }
}
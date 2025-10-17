using Wired.IO.Http11Express.Context;

namespace Wired.IO.Http11Express.Middleware;

public static class ServeSpaFiles
{
    public static async Task HandleAsync(Http11ExpressContext ctx)
    {
    }

    private static bool IsRouteFile(string route) => Path.HasExtension(route);
}
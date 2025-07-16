using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Http11.Context;
using Wired.IO.Http11.Middleware;

namespace Wired.IO.Utilities;

public static class MiddlewareUtilities
{
    public static void AddDefaultMiddleware<TContext>(this IServiceCollection services)
    {
        if (typeof(TContext) == typeof(Http11Context))
        {
            services
                .UseResponse()
                .ReadRequestBody();
        }
    }
}
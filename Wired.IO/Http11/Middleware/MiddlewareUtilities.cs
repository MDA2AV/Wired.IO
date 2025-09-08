using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Http11.Context;

namespace Wired.IO.Http11.Middleware;

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
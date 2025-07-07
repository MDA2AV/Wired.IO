using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Builder;
using Wired.IO.Http11.Context;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;

namespace Wired.IO.Http11.Middleware;

/// <summary>
/// Provides extension methods for registering middleware
/// into the <see cref="Wired.IO.Builder.Builder{THandler, TContext}"/> pipeline.
/// </summary>
public static class MiddlewareRegisterExtensions
{
    /// <summary>
    /// Adds response-writing middleware to the request processing pipeline.
    /// </summary>
    /// <remarks>
    /// This middleware runs after the next delegate in the pipeline and finalizes the HTTP response by:
    /// <list type="bullet">
    /// <item>Writing the HTTP status line.</item>
    /// <item>Injecting standard and user-defined HTTP headers.</item>
    /// <item>Handling content-length or chunked encoding based on response state.</item>
    /// <item>Streaming the response body content if present.</item>
    /// </list>
    ///
    /// It delegates to <see cref="ResponseMiddleware.HandleAsync(IContext, uint)"/> to write the complete HTTP/1.1 response.
    /// 
    /// This middleware is typically registered last to ensure the response is finalized after
    /// all earlier handlers and middleware have completed their logic.
    /// </remarks>
    /// <param name="builder">
    /// The <see cref="Builder{THandler, TContext}"/> to extend.
    /// </param>
    /// <returns>
    /// The same <see cref="Builder{THandler, TContext}"/> instance, allowing method chaining.
    /// </returns>
    public static Builder<WiredHttp11<Http11Context>, Http11Context> UseResponse(
        this Builder<WiredHttp11<Http11Context>, Http11Context> builder)
    {
        Func<IServiceProvider, Func<Http11Context, Func<Http11Context, Task>, Task>> func =
            scope => async (ctx, next) =>
            {
                //Console.WriteLine("UseResponse");

                await next(ctx);
                await ResponseMiddleware.HandleAsync(ctx);
            };

        builder.App.HostBuilder.ConfigureServices((_, services) =>
            services.AddScoped<Func<Http11Context, Func<Http11Context, Task>, Task>>(func));

        return builder;
    }

    /// <summary>
    /// Adds request body parsing middleware to the pipeline.
    /// </summary>
    /// <remarks>
    /// This middleware reads the HTTP request body before passing control to the next delegate in the pipeline.
    /// It supports both:
    /// <list type="bullet">
    /// <item><c>Content-Length</c>-based bodies</item>
    /// <item><c>Transfer-Encoding: chunked</c> bodies</item>
    /// </list>
    ///
    /// The raw request content is populated into <see cref="IRequest.Content"/> on the current <see cref="Http11Context"/>.
    /// 
    /// This should be placed early in the middleware pipeline for any application expecting to access request bodies.
    /// </remarks>
    /// <param name="builder">
    /// The <see cref="Builder{THandler, TContext}"/> to extend.
    /// </param>
    /// <returns>
    /// The same <see cref="Builder{THandler, TContext}"/> instance for fluent configuration.
    /// </returns>
    public static Builder<WiredHttp11<Http11Context>, Http11Context> ReadRequestBody(
        this Builder<WiredHttp11<Http11Context>, Http11Context> builder)
    {
        Func<IServiceProvider, Func<Http11Context, Func<Http11Context, Task>, Task>> func =
            scope => async (ctx, next) =>
            {
                //Console.WriteLine("ReadRequestBody");

                await RequestBodyMiddleware.HandleAsync(ctx);
                await next(ctx);
            };

        builder.App.HostBuilder.ConfigureServices((_, services) =>
            services.AddScoped<Func<Http11Context, Func<Http11Context, Task>, Task>>(func));

        return builder;
    }
}

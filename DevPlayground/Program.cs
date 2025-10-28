using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Wired.IO.App;
using Wired.IO.Builder;
using Wired.IO.Http11Express.Context;

internal class Program
{

    //**** DIOGO NOTE: TRY WITH TRANSIENT ENDPOINTS *****

    private static readonly Func<Http11ExpressContext, Func<Http11ExpressContext, Task>, Task> MiddlewareExample = async (ctx, next) =>
    {
        Console.WriteLine("Executing Manual Pipeline Middleware");
        await next(ctx);
    };

    public static async Task Main(string[] args)
    {
        var builder = WiredApp.CreateExpressBuilder();

        builder.Services.AddScoped<Dependency>();

        _ = builder
            .Port(8080)
            .NoScopedEndpoints()

            .AddManualPipeline(
                "/*", 
                [HttpConstants.Get, HttpConstants.Post, HttpConstants.Delete, HttpConstants.Put], 
                ctx =>
                {
                    Console.WriteLine("Running manual pipeline endpoint!");

                    ctx
                        .Respond()
                        .Type("text/plain"u8)
                        .Content("Hello from manual pipeline!"u8);

                    return Task.CompletedTask;
                }, [MiddlewareExample])

            .UseRootMiddleware(async (ctx, nxt) =>
            {
                Console.WriteLine("Executing root middleware!");
                await nxt(ctx);
            });

            _ = builder.MapGroup("/")
            .UseMiddleware(async (ctx, next) =>
            {
                Console.WriteLine("Executing Global middleware for group /");
                await next(ctx);
            });

            _ = builder.MapGroup("/api")
            .UseMiddleware(async (ctx, next) =>
            {
                Console.WriteLine("Executing Middleware 1");
                await next(ctx);
            })
            .MapPost("/json", ctx =>
            {
                //ctx.Services.GetRequiredService<Dependency>().Handle();
                ctx.Respond().Type("text/plain"u8).Content("Hello, World!"u8);
                return Task.CompletedTask;
            })

            .MapGroup("/v1")
            .UseMiddleware(async (ctx, next) =>
            {
                Console.WriteLine("Executing Middleware 2");
                await next(ctx);
            })
            .MapGet("/json", ctx =>
            {
                ctx.Services.GetRequiredService<Dependency>().Handle();
                ctx.Respond().Type("text/plain"u8).Content("Hello, World!"u8);
                return Task.CompletedTask;
            });

        _ = builder
            .MapGroup("/user")
            .MapGet("/json/:id", ctx =>
            {
                Console.WriteLine("Running user endpoint!");
                ctx.Respond().Type("text/plain"u8).Content("Hello, World! /user/json"u8);
                return Task.CompletedTask;
            });

        await builder
            .Build()
            .RunAsync();
    }

    public class Dependency : IDisposable
    {
        public string Handle() => "Hello from Dependency!";
        public void Dispose() => Console.WriteLine("Disposed Dependency");
    }
}

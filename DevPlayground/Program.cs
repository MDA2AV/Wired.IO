using Microsoft.Extensions.DependencyInjection;
using Wired.IO.App;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WiredApp.CreateExpressBuilder();

        builder.Services.AddScoped<Dependency>();

        _ = builder
            .Port(8080)
            .NoScopedEndpoints()
            .MapGroup("/")
            .UseMiddleware(async (ctx, next) =>
            {
                Console.WriteLine("Executing Middleware 1");
                await next(ctx);
            })
            .MapGet("/json", ctx =>
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
            .MapGet("/json", ctx =>
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

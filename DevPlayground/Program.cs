using Microsoft.Extensions.DependencyInjection;
using Wired.IO.App;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WiredApp.CreateExpressBuilder();

        builder.Services.AddScoped<Dependency>();

        var apiGroup = builder
            .Port(8080)
            .MapGroup("/api")
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
            });

        var apiSubGroup = apiGroup
            .MapGroup("/v1")
            .UseMiddleware(async (ctx, next) =>
            {
                Console.WriteLine("Executing Middleware 2");
                await next(ctx);
            })
            .MapGet("/json", ctx =>
            {
                Console.WriteLine("Running api v1 endpoint!");
                return Task.CompletedTask;
            });

        var userGroup = builder
            .MapGroup("/user")
            .MapGet("/json", ctx =>
            {
                Console.WriteLine("Running user endpoint!");
                return Task.CompletedTask;
            });

        await builder
            .Build2()
            .RunAsync();
    }

    public class Dependency : IDisposable
    {
        public string Handle() => "Hello from Dependency!";
        public void Dispose() => Console.WriteLine("Disposed Dependency");
    }
}

using Microsoft.Extensions.DependencyInjection;
using Wired.IO.App;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WiredApp.CreateExpressBuilder();

        builder.Services.AddScoped<Dependency>();

        var apiGroup = builder
            .MapGroup("/api")
            .MapGet("/json", ctx =>
            {
                ctx.Services.GetRequiredService<Dependency>().Handle();
                return Task.CompletedTask;
            });

        var apiSubGroup = apiGroup
            .MapGroup("/v1")
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

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wired.IO.App;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Protocol.Response;

var builder = App.CreateBuilder(); // Create a default builder, assumes HTTP/1.1

builder.App.HostBuilder
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information); // Set the minimum log level
    })
    .ConfigureServices((_, services) =>
    {
        services.AddScoped<DependencyService>();
    });

var app = builder
    .Port(5000) // Configured to http://localhost:5000
    .MapGet("/quick-start", scope => async httpContext =>
    {
        var dependency = scope.GetRequiredService<DependencyService>();
        dependency.Handle(); // Use the service

        httpContext
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json")
            .Content(new JsonContent(
                new { Name = "Alice", Age = 30 }, 
                JsonSerializerOptions.Default));
    })
    .Build();

await app.RunAsync();

class DependencyService(ILogger<DependencyService> logger) : IDisposable
{
    public void Handle() =>
        logger.LogInformation($"{nameof(DependencyService)} was handled.");
    public void Dispose() =>
        logger.LogInformation($"{nameof(DependencyService)} was disposed.");
}
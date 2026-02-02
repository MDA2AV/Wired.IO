using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wired.IO.App;
using Wired.IO.Handlers.Http11Express.Response.Content;
using Wired.IO.Protocol.Response;

// dotnet publish -f net10.0 -c Release /p:PublishAot=true /p:OptimizationPreference=Speed

var services = new ServiceCollection();

services.AddScoped<Service>();

var builder = WiredApp
    //.CreateOverclockedBuilder()
    .CreateRocketBuilder()
    //.CreateExpressBuilder()
    .NoScopedEndpoints()
    .Port(8080);

builder.EmbedServices(services);

builder
    .MapGroup("/")
    .MapGet("/route", context =>
    {
        JsonContext SerializerContext = JsonContext.Default;
        
        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json"u8)
            .Content(new ExpressJsonAotContent(new JsonMessage
            {
                Message = "Hello World!"
            }, SerializerContext.JsonMessage));
    });
    
builder
    .MapGroup("/api")
    .UseMiddleware(async (context, next) =>
    {
        // logger or any dependencies can be resolved using scope
        var logger = context.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            Console.WriteLine("Executing Middleware");
            // Execute next in line, could be another middleware or the endpoint
            await next(context);
        }
            
        catch (Exception e)
        {
            logger.LogError(e.Message);

            context.Respond()
                .Status(ResponseStatus.InternalServerError)
                .Type("application/json"u8)
                .Content(new ExpressJsonContent(new { Error = e.Message }));
        }
    })
    .MapGet("/my-endpoint", async context =>
    {
        await context.Services.GetRequiredService<Service>().HandleAsync();
        
        JsonContext SerializerContext = JsonContext.Default;
        
        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json"u8)
            .Content(new ExpressJsonAotContent(new JsonMessage
            {
                Message = "Hello World!"
            }, SerializerContext.JsonMessage));
    });

var provider = services.BuildServiceProvider();

await builder
    .Build(provider)
    .RunAsync();
    
public struct JsonMessage { public string Message { get; set; } }

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JsonMessage))]
public partial class JsonContext : JsonSerializerContext { }

public class Service : IDisposable
{
    public Service() => Console.WriteLine("Created Service");

    public async Task HandleAsync()
    {
        await Task.Delay(0);
        Console.WriteLine("Handled Service");
    }
    
    public void Dispose() => Console.WriteLine("Disposed Service");
}
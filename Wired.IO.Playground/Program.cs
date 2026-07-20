using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wired.IO.App;
using Wired.IO.Protocol.Response;

// dotnet publish -f net10.0 -c Release /p:PublishAot=true /p:OptimizationPreference=Speed

var builder = WiredOxide
    .CreateBuilder()
    .NoScopedEndpoints()
    .Port(8080);

builder.Services.AddScoped<Service>();
/*var builder = WiredApp
    .CreateExpressBuilder()
    .NoScopedEndpoints()
    .Port(8080);*/

//builder.EmbedServices(services);

builder
    .MapGroup("/")
    .MapGet("/route", context =>
    {
        context.Connection.Write("HTTP/1.1 200 OK\r\n"u8 +
                                 "Server: W\r\n"u8 +
                                 "Content-Length: 27\r\n"u8 +
                                 "Content-Type: application/json\r\n\r\n"u8 +
                                 "{\"Message\":\"Hello, World!\"}"u8);
        
        // ioxide tier: serialize via the source-gen context, then write the bytes into ioxide's write
        // slab (ctx.Connection is an IBufferWriter<byte>); the length sets Content-Length.
        /*
        var json = JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { Message = "Hello World!" }, JsonContext.Default.JsonMessage);
        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json"u8)
            .Content(() => context.Connection.Write(json), (ulong)json.Length);
        */
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

            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{e.Message}\"}}");
            context.Respond()
                .Status(ResponseStatus.InternalServerError)
                .Type("application/json"u8)
                .Content(() => context.Connection.Write(error), (ulong)error.Length);
        }
    })
    .MapGet("/my-endpoint", async context =>
    {
        await context.Services.GetRequiredService<Service>().HandleAsync();

        var json = JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { Message = "Hello World!" }, JsonContext.Default.JsonMessage);

        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json"u8)
            .Content(() => context.Connection.Write(json), (ulong)json.Length);
    });

await builder
    .Build()
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
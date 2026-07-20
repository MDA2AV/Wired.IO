using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.App;
using Wired.IO.Http11Express.Context;
using Wired.IO.Http11Express.Response.Content;
using Wired.IO.Http11Express.StaticHandlers;
using Wired.IO.Protocol.Response;

var services = new ServiceCollection();

var builder = WiredApp
    .CreateExpressBuilder<ExtendedContext>()
    .Port(8080)
    .AddStaticResourceProvider<ExtendedContext>("/resources/*",
        new Location
        {
            Assembly = Assembly.GetExecutingAssembly(),
            LocationType = LocationType.EmbeddedResource,
            Path = "Resources"
        }, []);

builder.EmbedServices(services);
    
builder
    .MapGroup("/")
    .MapGet("/my-endpoint", context =>
    {
        JsonContext SerializerContext = JsonContext.Default;
        
        var ip = ((IPEndPoint)context.Inner.RemoteEndPoint!).Address;
        Console.WriteLine($"Client: {ip.ToString()}");
        
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

public class ExtendedContext : Http11ExpressContext
{
    
}
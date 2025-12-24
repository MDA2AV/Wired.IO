using System.Text.Json.Serialization;
using Wired.IO.App;
using Wired.IO.Http11Express.Response.Content;
using Wired.IO.Protocol.Response;

var builder = WiredApp
    .CreateExpressBuilder()
    .Port(8080);
    
builder
    .MapGroup("/")
    .MapGet("/my-endpoint", context =>
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

await builder
    .Build()
    .RunAsync();
    
public struct JsonMessage { public string Message { get; set; } }

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JsonMessage))]
public partial class JsonContext : JsonSerializerContext { }

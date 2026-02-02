using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wired.IO.App;
using Wired.IO.Protocol.Response;

namespace OverclockedApp;

// dotnet publish -f net10.0 -c Release /p:PublishAot=true /p:OptimizationPreference=Speed

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Program.JsonMessage))]
public partial class JsonContext : JsonSerializerContext { }

public static class Program
{
    [ThreadStatic] private static Utf8JsonWriter? _tUtf8JsonWriter;
    public static async Task Main(string[] args)
    {
        await WiredApp
            .CreateOverclockedBuilder()  // io_uring
            .NoScopedEndpoints()
            .UseRootEndpoints()
            .Port(8080)
            .MapGet("/json", context =>
            {
                context.Respond()
                    .Status(ResponseStatus.Ok)
                    .Type("application/json"u8)
                    .Content(() =>
                    {
                        var utf8JsonWriter = _tUtf8JsonWriter ??= new Utf8JsonWriter(context.Connection, new JsonWriterOptions { SkipValidation = true });
                        utf8JsonWriter.Reset(context.Connection);
                        JsonSerializer.Serialize(utf8JsonWriter, new JsonMessage { Message = JsonBody }, SerializerContext.JsonMessage);
                    }, 27);
                
            })
            .Build()
            .RunAsync();
    }
    
    private const string JsonBody = "Hello, World!";
    public static readonly JsonContext SerializerContext = JsonContext.Default;
    public struct JsonMessage { public string Message { get; set; } }
}


/*
context.Connection.Write("HTTP/1.1 200 OK\r\n"u8 +
                         "Server: W\r\n"u8 +
                         "Content-Length: 27\r\n"u8 +
                         "Content-Type: application/json\r\n\r\n"u8 +
                         "{\"Message\":\"Hello, World!\"}"u8);
*/

/*
var headers =
    "HTTP/1.1 200 OK\r\n"u8 +
    "Server: W\r\n"u8 +
    "Content-Length: 27\r\n"u8 +
    "Content-Type: application/json\r\n"u8;

context.Connection.Write(headers);
context.Connection.Write(DateHelper.HeaderBytes);

_tUtf8JsonWriter ??= new Utf8JsonWriter(context.Connection, new JsonWriterOptions { SkipValidation = true });
_tUtf8JsonWriter.Reset(context.Connection);

// Creating(Allocating) a new JsonMessage every request
var message = new JsonMessage { Message = "Hello, World!" };
// Serializing it every request
JsonSerializer.Serialize(_tUtf8JsonWriter, message, SerializerContext.JsonMessage);
*/
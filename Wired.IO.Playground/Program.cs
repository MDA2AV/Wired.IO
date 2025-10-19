using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Wired.IO.App;
using Wired.IO.Common.Attributes;
using Wired.IO.Http11.Context;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Http11.Websockets;
using Wired.IO.Http11Express;
using Wired.IO.Http11Express.BuilderExtensions;
using Wired.IO.Http11Express.Context;
using Wired.IO.Http11Express.Response.Content;
using Wired.IO.Http11Express.StaticHandlers;
using Wired.IO.Mediator;
using Wired.IO.Playground;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Protocol.Writers;
using Wired.IO.Utilities;
using Wired.IO.WiredEvents;


[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Program.JsonMessage))]
internal partial class JsonContext : JsonSerializerContext
{
}


internal class Program
{
    private static ReadOnlySpan<byte> _plaintextPreamble =>
        "HTTP/1.1 200 OK\r\n"u8 +
        "Server: K\r\n"u8 +
        "Content-Type: text/plain\r\n"u8 +
        "Content-Length: 13\r\n\r\n"u8;

    private static ReadOnlySpan<byte> _plainTextBody => "Hello, World!"u8;
    private static ReadOnlySpan<byte> _jsonPreamble =>
        "HTTP/1.1 200 OK\r\n"u8 +
        "Server: K\r\n"u8 +
        "Content-Type: application/json\r\n"u8 +
        "Content-Length: 27\r\n\r\n"u8;

    private const string JsonBody = "Hello, World!";
    
    [ThreadStatic]
    private static Utf8JsonWriter? t_writer;
    
    public static readonly DefaultObjectPool<ChunkedWriter> ChunkedWriterPool
        = new(new ChunkedWriterObjectPolicy());
    
    private sealed class ChunkedWriterObjectPolicy : IPooledObjectPolicy<ChunkedWriter>
    {
        public ChunkedWriter Create() => new();
        public bool Return(ChunkedWriter writer)
        {
            writer.Reset();
            return true;
        }
    }
    
    public struct JsonMessage
    {
        public string Message { get; set; }
    }
    
    private static readonly JsonContext SerializerContext = JsonContext.Default;

    public Action<PipeWriter, JsonMessage> Handler;

    private static readonly Action<PipeWriter, JsonMessage> StaticHandler = HandleFast;
    private static readonly Action<PipeWriter, string> StaticHandlerString = HandleJson;

    private static Action CreateBoundHandler(PipeWriter writer, JsonMessage message) => () => StaticHandler.Invoke(writer, message);
    private static Action CreateBoundHandlerString(PipeWriter writer, string serialized) => () => StaticHandlerString.Invoke(writer, serialized);

    private static void HandleFast(PipeWriter writer, JsonMessage message)
    {
        var utf8JsonWriter = t_writer ??= new Utf8JsonWriter(writer, new JsonWriterOptions { SkipValidation = true });
        utf8JsonWriter.Reset(writer);
        JsonSerializer.Serialize(utf8JsonWriter, message, SerializerContext.JsonMessage);
    }
    
    private static void HandleChunked(PipeWriter writer, JsonMessage message)
    {
        var chunkedWriter = ChunkedWriterPool.Get();
        chunkedWriter.SetOutput(writer);
        t_writer ??= new Utf8JsonWriter(chunkedWriter, new JsonWriterOptions { SkipValidation = true });
        t_writer.Reset(chunkedWriter);
        
        JsonSerializer.Serialize(t_writer, message, SerializerContext.JsonMessage);
        
        chunkedWriter.Complete();
        ChunkedWriterPool.Return(chunkedWriter);
    }
    
    private static void HandleJson(PipeWriter writer, string serialized)
    {
        writer.Write(Encoders.Utf8Encoder.GetBytes(serialized));
    }
    
    public static async Task Main(string[] args)
    {
        var expressBuilder = WiredApp.CreateExpressBuilder();

        await expressBuilder
            .Port(8080)
            .ServeSpaFilesExpress("/", new Location
            {
                LocationType = LocationType.FileSystem,
                Path = "E:/VS/angular"
            })
            .ServeSpaFilesExpress("/res", new Location
            {
                LocationType = LocationType.EmbeddedResource,
                Assembly = Assembly.GetExecutingAssembly(),
                Path = "Angular"
            })
            .MapGet("/jsonRaw", scope => async ctx =>
            {
                //ctx.Writer.Write("HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=UTF-8\r\nContent-Length: 27\r\n\r\n{\"message\":\"Hello, World!\"}\r\n"u8);
                /*
                ctx.Writer.Write(_jsonPreamble);
                var utf8JsonWriter = t_writer ??= new Utf8JsonWriter(ctx.Writer, new JsonWriterOptions { SkipValidation = true });
                utf8JsonWriter.Reset(ctx.Writer);
                JsonSerializer.Serialize(utf8JsonWriter, new JsonMessage { Message = "Hello, World!" }, SerializerContext.JsonMessage);
                */
                
                var payload = new JsonMessage { Message = JsonBody };
                var myHandler = CreateBoundHandler(ctx.Writer, payload);

                ctx
                    .Respond()
                    .Type("application/json"u8)
                    .Content(myHandler, 27);

            })
            .MapGet("/jsonRaw2", scope => async ctx =>
            {
                var payload = JsonSerializer.Serialize(new JsonMessage { Message = JsonBody },
                    SerializerContext.JsonMessage);
                var myHandler = CreateBoundHandlerString(ctx.Writer, payload);

                ctx
                    .Respond()
                    .Type("application/json"u8)
                    .Content(myHandler, (ulong)payload.Length);

            })
            .MapGet("/plaintext", scope => async ctx =>
            {
                ctx.Writer.Write(_plaintextPreamble);
                ctx.Writer.Write(_plainTextBody);
            })
            .MapGet("/string", scope => async ctx =>
            {
                ctx
                    .Respond()
                    .Type("application/json"u8)
                    //.Content(new ExpressStringContent("\"message\": \"Hello, World!\""));
                    .Content(new ExpressStringContent(JsonSerializer.Serialize(new JsonMessage { Message = JsonBody }, SerializerContext.JsonMessage)));
            })
            .MapGet("/jsonu8", scope => async ctx =>
            {
                ctx
                    .Respond()
                    .Type("application/json"u8)
                    .Content("\"message\": \"Hello, World!\""u8);
            })
            .MapGet("/jsonObj", scope => async ctx =>
            {
                ctx
                    .Respond()
                    .Type("application/json"u8)
                    .Content(new ExpressJsonObjectContent(new JsonMessage { Message = JsonBody }));
            })
            .MapGet("/jsonS", scope => async ctx =>
            {
                ctx
                    .Respond()
                    .Type("application/json"u8)
                    .Content(new ExpressStringContent(JsonSerializer.Serialize(new JsonMessage { Message = JsonBody })));
                    //.Content(new ExpressJsonContent(JsonSerializer.Serialize(new JsonMessage { Message = JsonBody }, SerializerContext.JsonMessage)));
            })
            .MapGet("/jsonCacheS", scope => async ctx =>
            {
                ctx
                    .Respond()
                    .Type("application/json"u8)
                    .Content<ExpressStringContent, string>(
                        (content, payload) => content.Set(payload) , 
                        JsonSerializer.Serialize(new JsonMessage { Message = JsonBody }, SerializerContext.JsonMessage),
                        (payload, length) => new ExpressStringContent(payload));
                
            })
            .MapGet("/json", scope => async ctx =>
            {
                ctx.Respond()
                    .Type("application/json"u8)
                    .Content(new ExpressJsonContent<JsonMessage>(new JsonMessage { Message = JsonBody }, SerializerContext.JsonMessage));
            })
            .MapGet("/jsonCache", scope => async ctx =>
            {
                ctx.Respond()
                    .Type("application/json"u8)
                    .Content<ExpressJsonContent<JsonMessage>, JsonMessage>(
                        (content, payload, typeInfo) => content.Set(payload, typeInfo),
                        new JsonMessage { Message = JsonBody }, 
                        SerializerContext.JsonMessage,
                        (payload, typeInfo) => new ExpressJsonContent<JsonMessage>(payload, typeInfo));
            })
            .Build()
            .RunAsync();
    }

    public static async Task Main1(string[] args)
    {
        var builder = WiredApp.CreateBuilder();

        await builder
            .Port(8080)
            .MapGet("/json", scope => async ctx =>
            {
                ctx.Writer.Write("HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=UTF-8\r\nContent-Length: 28\r\n\r\n {\"message\":\"Hello, World!\"}\r\n"u8);
                await ctx.Writer.FlushAsync();
            })
            .MapGet("/jsor", scope => ctx =>
            {
                ctx.Respond()
                    .Status(ResponseStatus.Ok)
                    .Content(new JsonContent(
                    new
                    {
                        Message = "Hello, World!"
                    }, JsonSerializerOptions.Default))
                    .Type("application/json");
            })
            .Build()
            .RunAsync();
    }
}


/*
var serviceCollection = new ServiceCollection();

serviceCollection
    .AddScoped<DependencyService>()
    .AddWiredEventHandler<ExampleWiredEvent, ExampleWiredEventHandler>()
    .AddScoped(typeof(IPipelineBehavior<>), typeof(ExampleBehavior<>))
    .AddScoped(typeof(IPipelineBehaviorNoResponse<RequestQuery3>), typeof(ExampleBehavior8<RequestQuery3>))
    .AddScoped(typeof(IPipelineBehavior<,>), typeof(ExampleBehavior<,>));

var builder = WiredApp.CreateBuilder(); // Create a default builder, assumes HTTP/1.1

builder.Services
    .AddLogging(loggingBuilder =>
    {
        loggingBuilder
            .ClearProviders()
            .SetMinimumLevel(LogLevel.Trace)
            .AddConsole();
    })
    .AddScoped<DependencyService>()
    .AddWiredEventHandler<ExampleWiredEvent, ExampleWiredEventHandler>()
    .AddScoped(typeof(IPipelineBehavior<>), typeof(ExampleBehavior<>))
    .AddScoped(typeof(IPipelineBehaviorNoResponse<RequestQuery3>), typeof(ExampleBehavior8<RequestQuery3>))
    .AddScoped(typeof(IPipelineBehavior<,>), typeof(ExampleBehavior<,>));

builder
    .AddWiredEvents(dispatchContextWiredEvents: false)
    .AddHandlers(Assembly.GetExecutingAssembly())
    .Port(5000) // Configured to http://localhost:5000
    .MapGet("/quick-start", scope => async httpContext =>
    {
        //httpContext.AddWiredEvent(new ExampleWiredEvent("Creating a wired event"));

        var service = scope.GetRequiredService<DependencyService>();
        service.Handle();

        var entity = new Entity();
        entity.DoSomething();

        //await Task.Delay(5000, httpContext.CancellationToken);

        httpContext
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json")
            .Content(new JsonContent(
                new { Name = "Alice", Age = 30 },
                JsonSerializerOptions.Default));

        var wiredEventDispatcher = scope.GetRequiredService<Func<IEnumerable<IWiredEvent>, Task>>();
        await wiredEventDispatcher(entity.WiredEvents);
        entity.ClearWiredEvents();
    })
    .MapGet("/quick-start2", scope => async httpContext =>
    {
        await httpContext
            .Writer.WriteAsync(
                "HTTP/1.1 200 OK\r\nContent-Length:0\r\nContent-Type: application/json\r\nConnection: keep-alive\r\n\r\n"u8
                    .ToArray());
    })
    .MapGet("/quick-start3", scope => async httpContext =>
    {
        httpContext
            .Writer.Write("HTTP/1.1 200 OK\r\n"u8);
        httpContext
            .Writer.Write("Content-Length:0\r\n"u8);
        httpContext
            .Writer.Write("Content-Type: application/json\r\nConnection: keep-alive\r\n\r\n"u8);

        await httpContext.Writer.FlushAsync();
    })
    .MapGet("/handler", scope => async context =>
    {
        //var requestHandler = scope.GetRequiredService<IRequestHandler<RequestQuery, RequestResult>>();
        //var result = await requestHandler.Handle(new RequestQuery(), context.CancellationToken);

        var requestHandler = scope.GetRequiredService<IRequestDispatcher<Http11Context>>();
        var result = await requestHandler.Send(new RequestQuery());

        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json")
            .Content(new JsonContent(result, JsonSerializerOptions.Default));
    })
    .MapGet("/handler3", scope => async context =>
    {
        Console.WriteLine("handler3");

        var dispatcher = scope.GetRequiredService<IRequestDispatcher<Http11Context>>();
        await dispatcher.Send(new RequestQuery2(), context.CancellationToken);

        context
            .Respond()
            .Status(ResponseStatus.Ok);
    })
    .MapGet("/hybrid-endpoint", scope => async context =>
    {
        // Resolve your IRequestHandler
        var requestHandler = scope
            .GetRequiredService<IRequestHandler<RequestQuery2>>();

        // Execute it
        await requestHandler
            .Handle(new RequestQuery2(), context.CancellationToken);

        // Pass its result top be processed by the response middleware,
        // optionally deal with the response yourself without calling .Respond()
        context
            .Respond()
            .Status(ResponseStatus.NoContent);
        //.Type("application/json")
        //.Content(new JsonContent(new { Result = "Ok" }, JsonSerializerOptions.Default));
    })
    .MapGet("/websocket", scope => async context =>
    {
        var arrayPool = ArrayPool<byte>.Shared;
        var buffer = arrayPool.Rent(8192);

        while (true)
        {
            (ReadOnlyMemory<byte> data, WsFrameType wsFrameType) receivedData = await context.WsReadAsync(buffer);

            if (receivedData.wsFrameType == WsFrameType.Close)
                break;

            if (receivedData.data.IsEmpty)
                break;

            await context.WsSendAsync(receivedData.Item1, 0x01);
        }

        arrayPool.Return(buffer);
    })
    .MapGet("/wired-event-handler", scope => async context =>
    {
        var requestHandler = scope.GetRequiredService<IRequestDispatcher<Http11Context>>();
        var result = await requestHandler.Send(new RequestQuery(), context.CancellationToken);

        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json")
            .Content(new JsonContent(result, JsonSerializerOptions.Default));
    })
    .MapGet("/custom-content", scope => async context =>
    {
        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("text/plain")
            .Content(new CustomResponseContent("response data"u8.ToArray()));
    })
    .UseMiddleware(scope => async (context, next) =>
    {
        var logger = scope.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Middleware");

        try
        {
            await next(context);
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);

            context.Respond()
                .Status(ResponseStatus.InternalServerError)
                .Type("application/json")
                .Content(new JsonContent(new { Error = e.Message }, JsonSerializerOptions.Default));
        }
    });

var serviceProvider = serviceCollection.BuildServiceProvider();

var app = builder.Build();

await app.RunAsync();

public class DependencyService(ILogger<DependencyService> logger) : IDisposable
{
    public void Handle()
    {
        logger.LogInformation($"{nameof(DependencyService)} was handled.");
    }

    public void Dispose()
    {
        logger.LogInformation($"{nameof(DependencyService)} was disposed.");
    }
}

public class RequestHandlerExample(Func<IEnumerable<IWiredEvent>, Task> wiredEventDispatcher) 
    : IRequestHandler<RequestQuery, RequestResult>
{
    public async Task<RequestResult> Handle(RequestQuery request, CancellationToken cancellationToken)
    {
        var entity = new Entity();
        entity.DoSomething();

        await wiredEventDispatcher(entity.WiredEvents);
        entity.ClearWiredEvents();

        return new RequestResult("Toni", "Mars");
    }
}
public record RequestQuery() : IRequest<RequestResult>;

public record RequestResult(string Name, string Address);

public class RequestHandlerExample3 : IRequestHandler<RequestQuery2>
{
    public async Task Handle(RequestQuery2 request, CancellationToken cancellationToken)
    {
        Console.WriteLine("RequestHandlerExample3");

        await Task.Delay(0, cancellationToken); // Do work
    }
}
public record RequestQuery2() : IRequest;


[Route("GET", "/handler2")]
public class RequestHandlerExample2(Func<IEnumerable<IWiredEvent>, Task> wiredEventDispatcher) : IContextHandler<Http11Context>
{
    public async Task Handle(Http11Context context, CancellationToken cancellationToken)
    {
        var entity = new Entity();
        entity.DoSomething();

        await wiredEventDispatcher(entity.WiredEvents);
        entity.ClearWiredEvents();

        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("application/json")
            .Content(new JsonContent(new { Name = "Toni" }, JsonSerializerOptions.Default));
    }
}

public class ExampleBehavior<TContext> : IPipelineBehavior<TContext>
    where TContext : IContext
{
    public async Task Handle(TContext context, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        Console.WriteLine("IPipelineBehavior<Http11Context>");
        // Execute pre endpoint behavior logic here

        await next();

        // Execute post endpoint behavior logic here
    }
}

public class ExampleBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> 
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Execute pre endpoint behavior logic here

        return await next();

        // Execute post endpoint behavior logic here
    }
}

public class ExampleBehavior8<TRequest> : IPipelineBehaviorNoResponse<RequestQuery3>
    where TRequest : IRequest
{
    public async Task Handle(RequestQuery3 request, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        Console.WriteLine("Ooga");
        // Execute pre endpoint behavior logic here

        await next();

        // Execute post endpoint behavior logic here
    }
}

public record RequestQuery3() : IRequest;
*/
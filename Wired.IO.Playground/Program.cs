﻿using System.Buffers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Wired.IO.App;
using Wired.IO.Common.Attributes;
using Wired.IO.Http11.Context;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Http11.Websockets;
using Wired.IO.Mediator;
using Wired.IO.Playground;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.WiredEvents;

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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wired.IO.App;
using Wired.IO.Protocol.Response;


var services = new ServiceCollection();
var builder = WiredApp
    .CreateExpressBuilder()
    .Port(8080)
    .EmbedServices(services)
    .UseRootEndpoints()
    .MapGet("/", context =>
    {
        var logger = context.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("WiredApp started");
        
        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("text/plain"u8)
            .Content("Hello World!"u8);
    });

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.SetMinimumLevel(LogLevel.Critical);
});

var provider = services.BuildServiceProvider();
    
await builder.Build(provider)
    .RunAsync();
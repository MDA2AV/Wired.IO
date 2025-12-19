using Wired.IO.App;
using Wired.IO.Protocol.Response;

await WiredApp
    .CreateExpressBuilder()
    .Port(8080)
    .UseRootEndpoints()
    .MapGet("/", context =>
    {
        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("text/plain"u8)
            .Content("Hello World!"u8);
    })
    .Build()
    .RunAsync();
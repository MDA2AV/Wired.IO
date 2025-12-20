## Wired.IO

[![NuGet](https://img.shields.io/nuget/v/Wired.IO.svg)](https://www.nuget.org/packages/Wired.IO/)

[Full project documentation](https://mda2av.github.io/Wired.IO.Docs/)

**Wired.IO** is a lightweight, high-performance, MIT licensed HTTP server framework for .NET. Designed from the ground up for **embedding**, **extensibility**, and **raw speed**, it gives you full control over your request pipeline without the weight of traditional web frameworks.

Whether you're building APIs, embedded servers, developer tools, or hybrid applications, Wired.IO provides a focused, zero-friction foundation that runs anywhere your .NET code does - no external hosting required.

## Existing Main Features

 - **Http/1.1 Support out of the box**
 - **Native AoT support**
 - **Custom Http Handlers for custom Http/x protocols**
 - **Baked in Dependency Injection/IoC Container with IServiceCollecion/IServiceProvider**
 - **Full Secure/TLS**
 - **Full Custom Middleware**
 - **Native ILoggingFactory**
 - **Static Resource Hosting**
 - **SPA, MPA Hosting**
 - **Wired Events for Event Driven Design**
 - **Embeddable with exising Apps**

## Why Wired.IO?

Unlike other lightweight web servers such as NetCoreServer or EmbedIO, Wired.IO is built directly on top of .NET’s IServiceCollection and integrates seamlessly with the standard dependency injection system. This design enables Wired.IO to provide the same modularity, extensibility, and testability benefits as ASP.NET Core, while still being extremely lightweight and embeddable. In contrast, other alternatives often rely on custom service registration patterns or lack DI support entirely, making them harder to scale or integrate cleanly with modern application architectures. With Wired.IO, developers can reuse familiar patterns like constructor injection, scoped services, middleware, and configuration, gaining the flexibility of ASP.NET Core with the performance and simplicity of a microserver.

- ⚡ **Fast by default** – Built on `System.IO.Pipelines` and optimized for low allocations and high throughput.
- 🧩 **Fully embeddable** – Add a production-ready HTTP server directly into your desktop, mobile, or console app.
- 🧵 **Lean and composable** – Define only what you need: your context, your pipeline, your handlers.
- 🔧 **Customizable by design** – TLS, routing, DI, and middleware are all open and easily replaceable.
- 🪶 **No runtime magic** – Everything is explicit. No black boxes, no surprises.


## Quick Start

```csharp
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
        context
            .Respond()
            .Status(ResponseStatus.Ok)
            .Type("text/plain"u8)
            .Content(new ExpressStringContent("My endpoint!"));
    });
    
await builder
    .Build()
    .RunAsync();
```

### Extending apps with functionality provided by GenHTTP

The [GenHTTP adapter](https://github.com/Kaliumhexacyanoferrat/wired-genhttp-adapter) allows you to add modules provided by the [GenHTTP framework]([https://genhttp.org](https://genhttp.org/documentation/content/)) to your Wired.IO app. After adding the corresponding nuget package, you can reference and map those handlers on your app:

```csharp
using GenHTTP.Adapters.WiredIO;
using GenHTTP.Modules.Functional;
using Wired.IO.App;

// GET http://localhost:5000/api/hello?a=World

var api = Inline.Create()
                .Get("hello", (string a) => $"Hello {a}!")
                .Defaults(); // adds compression, eTag handling, ...

var builder = WiredApp.CreateExpressBuilder()
                      .Port(5000)
                      .Map("/api", api);

var app = builder.Build();

await app.RunAsync();
```

## Thanks

- This project includes code originating from [GenHTTP](https://github.com/Kaliumhexacyanoferrat/GenHTTP): `PooledDictionary.cs`, `PoolBufferedStream.cs`, `ContentType.cs`

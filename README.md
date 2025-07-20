[![NuGet](https://img.shields.io/nuget/v/Wired.IO.svg)](https://www.nuget.org/packages/Wired.IO/)

[Full project documentation](https://mda2av.github.io/Wired.IO.Docs/)

**Wired.IO** is a lightweight, high-performance HTTP server framework for .NET. Designed from the ground up for **embedding**, **extensibility**, and **raw speed**, it gives you full control over your request pipeline without the weight of traditional web frameworks.

Whether you're building APIs, embedded servers, developer tools, or hybrid applications, Wired.IO provides a focused, zero-friction foundation that runs anywhere your .NET code does — no external hosting required.

## Existing Main Features

 - **Http/1.1**
 - **Custom Http Handlers for custom Http/x protocols**
 - **Inbuilt Dependency Injection/IoC Container with IServiceCollecion/IServiceProvider**
 - **Fast/Minimal and Mediator-like Endpoints**
 - **Full Secure/TLS**
 - **Full Custom Middleware**
 - **Pipeline Behaviors Support with Mediator**
 - **Native ILoggingFactory**
 - **Static Resource Hosting**
 - **Websockets RFC 6455**
 - **Wired Events for Event Driven Design**
 - **Embeddable with exising Apps**

## Upcoming features

 - **Compression** (planned, needs some research for mobile app compatibility)
 - **ETag** caching (planned next release)
 - **JWT Support** (planned)
 - **CORS Support** (planned next release)
 - **form-data support** (not planned, low priority)

## Why Wired.IO?

Unlike other lightweight web servers such as NetCoreServer or EmbedIO, Wired.IO is built directly on top of .NET’s IServiceCollection and integrates seamlessly with the standard dependency injection system. This design enables Wired.IO to provide the same modularity, extensibility, and testability benefits as ASP.NET Core, while still being extremely lightweight and embeddable. In contrast, other alternatives often rely on custom service registration patterns or lack DI support entirely, making them harder to scale or integrate cleanly with modern application architectures. With Wired.IO, developers can reuse familiar patterns like constructor injection, scoped services, middleware, and configuration, gaining the flexibility of ASP.NET Core with the performance and simplicity of a microserver.

- ⚡ **Fast by default** – Built on `System.IO.Pipelines` and optimized for low allocations and high throughput.
- 🧩 **Fully embeddable** – Add a production-ready HTTP server directly into your desktop, mobile, or console app.
- 🧵 **Lean and composable** – Define only what you need: your context, your pipeline, your handlers.
- 🔧 **Customizable by design** – TLS, routing, DI, and middleware are all open and easily replaceable.
- 🌐 **Hybrid app ready** – Serve a full **web-based frontend** from inside your app. Pair your MAUI or desktop backend with a modern SPA or HTML/JS UI — all self-hosted.
- 🪶 **No runtime magic** – Everything is explicit. No black boxes, no surprises.

## Built for Embedding

Wired.IO was created to **run inside your app**, not alongside it. This means you can:
- Run an HTTP interface inside a background service, tool, or MAUI app.
- Use it for internal tooling, configuration UIs, simulators, or control panels.
- Serve static files, WebSockets, or JSON APIs directly from your executable.
- Create **hybrid apps** with native backends and web-based frontends, served over `localhost`.

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.App;
using Wired.IO.Protocol.Response;

namespace IoxideApp;

// A service registered in DI and resolved inside a handler via ctx.Services — proves the new ioxide
// tier reuses Wired's dependency-injection container unchanged.
public interface IGreeter
{
    string Greet();
}

public sealed class Greeter : IGreeter
{
    public string Greet() => "hello from a DI-resolved service";
}

public static class Program
{
    private static readonly byte[] Plain = "Hello from the Wired.IO ioxide + Glyph11 tier\n"u8.ToArray();
    private static readonly byte[] Json  = "{\"message\":\"Hello, World!\"}"u8.ToArray(); // 27 bytes
    private static readonly byte[] MwBody = "handled-by-middleware (short-circuit)\n"u8.ToArray();

    public static async Task Main(string[] args)
    {
        var builder = WiredIoxide
            .CreateBuilder()   // ioxide transport + Glyph11 parse; App / DI / middleware / Map are Wired's
            .NoScopedEndpoints()
            .UseRootEndpoints()
            .Port(8080);

        // (1) DI — register a service; resolved per request via ctx.Services below.
        builder.Services.AddSingleton<IGreeter, Greeter>();

        // (2) Middleware — Wired's pipeline runs over the ioxide context. Reads the request, and can
        //     short-circuit before the endpoint. Proves middleware drives the new tier unchanged.
        builder.UseRootMiddleware(async (ctx, next) =>
        {
            Console.WriteLine($"[middleware] {ctx.Request.HttpMethod} {ctx.Request.Route}");
            if (ctx.Request.Route == "/mw")
            {
                ctx.Respond()
                    .Status(ResponseStatus.Ok)
                    .Type("text/plain"u8)
                    .Content(() => ctx.Connection.Write(MwBody), (ulong)MwBody.Length);
                return; // short-circuit: the endpoint is never reached
            }

            await next(ctx);
        });

        // (3) Baseline.
        builder.MapGet("/plaintext", ctx =>
        {
            ctx.Respond()
                .Status(ResponseStatus.Ok)
                .Type("text/plain"u8)
                .Content(() => ctx.Connection.Write(Plain), (ulong)Plain.Length);
        });

        // (4) Reads the Glyph11 BinaryRequest straight off the context (ctx.Binary) — method, path,
        //     header count, and a specific header — all zero-copy views over the recv buffer.
        builder.MapGet("/request", ctx =>
        {
            var method = Encoding.ASCII.GetString(ctx.Binary.Method.Span);
            var path = Encoding.ASCII.GetString(ctx.Binary.Path.Span);
            var headers = ctx.Binary.Headers;

            var host = "(none)";
            for (var i = 0; i < headers.Count; i++)
            {
                var kv = headers[i];
                if (Ascii.EqualsIgnoreCase(kv.Key.Span, "host"u8))
                {
                    host = Encoding.ASCII.GetString(kv.Value.Span);
                    break;
                }
            }

            var body = Encoding.UTF8.GetBytes(
                $"ctx.Binary (Glyph11 BinaryRequest) -> method={method} path={path} headerCount={headers.Count} host={host}\n");

            ctx.Respond()
                .Status(ResponseStatus.Ok)
                .Type("text/plain"u8)
                .Content(() => ctx.Connection.Write(body), (ulong)body.Length);
        });

        // (5) Resolves a service from DI inside the handler.
        builder.MapGet("/di", ctx =>
        {
            var greeter = ctx.Services.GetRequiredService<IGreeter>();
            var body = Encoding.UTF8.GetBytes(greeter.Greet() + "\n");

            ctx.Respond()
                .Status(ResponseStatus.Ok)
                .Type("text/plain"u8)
                .Content(() => ctx.Connection.Write(body), (ulong)body.Length);
        });

        // (6) JSON written into ioxide's write slab (Connection is an IBufferWriter<byte>).
        builder.MapGet("/json", ctx =>
        {
            ctx.Respond()
                .Status(ResponseStatus.Ok)
                .Type("application/json"u8)
                .Content(() => ctx.Connection.Write(Json), (ulong)Json.Length);
        });

        Console.WriteLine("IoxideApp — Wired.IO ioxide + Glyph11 tier — http://0.0.0.0:8080");
        Console.WriteLine("  /plaintext  /request  /di  /json  /mw");

        await builder.Build().RunAsync();
    }
}

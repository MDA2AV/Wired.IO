using Glyph11.Protocol;
using ioxide;
using Wired.IO.Handlers.Http11Oxide.Request;
using Wired.IO.Handlers.Http11Oxide.Response;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;

namespace Wired.IO.Handlers.Http11Oxide.Context;

/// <summary>
/// Context for the ioxide + Glyph11 tier. <see cref="Binary"/> IS the Glyph11 <see cref="BinaryRequest"/>
/// (zero-copy Method/Path/QueryParameters/Headers/Body over the recv buffer) — endpoint handlers read the
/// request straight off it (<c>ctx.Binary.Method</c>, <c>ctx.Binary.Path</c>, …). Everything else (DI via
/// <see cref="Services"/>, the response builder, routing via <see cref="Request"/>) is Wired's, unchanged.
/// </summary>
public class Http11OxideContext : IBaseContext<IBaseRequest, IOxideResponse>
{
    public Connection Connection { get; internal set; } = null!;

    /// <summary>Materialized method/route strings the Wired router matches on (set by the handler).</summary>
    public IBaseRequest Request { get; } = new Http11OxideRequest();

    /// <summary>The Glyph11-parsed request. Reused per request (cleared + reparsed by the handler).</summary>
    public BinaryRequest Binary { get; } = new();

    // Per-connection recv accumulation buffer (pooled with the context; grows as needed).
    internal byte[] Carry = new byte[16 * 1024];

    public IOxideResponse? Response { get; private set; }

    private OxideResponseBuilder? _responseBuilder;
    private OxideResponseBuilder ResponseBuilder => _responseBuilder ??= new OxideResponseBuilder(Response!);

    public OxideResponseBuilder Respond()
    {
        Response ??= new Http11OxideResponse();
        Response.Activate();
        return ResponseBuilder;
    }

    public IServiceProvider Services { get; set; } = null!;

    public CancellationToken CancellationToken { get; set; }

    public void Clear()
    {
        Request.Clear();
        Response?.Clear();
    }

    public void Dispose() { }
}

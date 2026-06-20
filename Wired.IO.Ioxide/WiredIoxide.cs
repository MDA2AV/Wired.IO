using System.Net.Security;

using Wired.IO.Builder;
using Wired.IO.Handlers.Http11Ioxide;
using Wired.IO.Handlers.Http11Ioxide.Context;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Transport.Ioxide;

namespace Wired.IO.App;

/// <summary>
/// Entry point for the ioxide + Glyph11 tier (the <c>Http11Ioxide</c> handler on the io_uring
/// <c>ioxide</c> engine). Mirrors <see cref="WiredApp"/>'s factories, but lives in the standalone
/// net11-only <c>Wired.IO.Ioxide</c> assembly since ioxide targets net11.0 only. The full Wired
/// framework (DI, middleware, routing, the Map API) is reused unchanged; only the transport + request
/// parse are ioxide-native, and the Glyph11 <c>BinaryRequest</c> is exposed on the context
/// (<c>ctx.Binary</c>).
/// </summary>
public static class WiredIoxide
{
    public static Builder<WiredHttp11Ioxide, Http11IoxideContext> CreateBuilder()
    {
        var builder = new Builder<WiredHttp11Ioxide, Http11IoxideContext>(() => new WiredHttp11Ioxide(),
            [SslApplicationProtocol.Http11], new IoxideTransport<Http11IoxideContext>());

        // Default 404 for unmatched routes (mirrors CreateExpressBuilder).
        return builder.MapFlowControl("NotFound", static ctx =>
        {
            ctx.Respond().Status(ResponseStatus.NotFound).Content(static () => { }, 0);
        });
    }
}

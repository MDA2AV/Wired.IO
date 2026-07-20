using System.Net.Security;

using Wired.IO.Builder;
using Wired.IO.Handlers.Http11Oxide;
using Wired.IO.Handlers.Http11Oxide.Context;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;
using Wired.IO.Transport.Oxide;

namespace Wired.IO.App;

/// <summary>
/// Entry point for the ioxide + Glyph11 tier (the <c>Http11Oxide</c> handler on the io_uring
/// <c>ioxide</c> engine). Mirrors <see cref="WiredApp"/>'s factories. The full Wired
/// framework (DI, middleware, routing, the Map API) is reused unchanged; only the transport + request
/// parse are ioxide-native, and the Glyph11 <c>BinaryRequest</c> is exposed on the context
/// (<c>ctx.Binary</c>).
/// </summary>
public static class WiredOxide
{
    public static Builder<WiredHttp11Oxide, Http11OxideContext> CreateBuilder()
    {
        var builder = new Builder<WiredHttp11Oxide, Http11OxideContext>(() => new WiredHttp11Oxide(),
            [SslApplicationProtocol.Http11], new OxideTransport<Http11OxideContext>());

        // Default 404 for unmatched routes (mirrors CreateExpressBuilder).
        return builder.MapFlowControl("NotFound", static ctx =>
        {
            ctx.Respond().Status(ResponseStatus.NotFound).Content(static () => { }, 0);
        });
    }
}

using Xunit;
using Wired.IO.Builder;
using Wired.IO.Http11;
using Wired.IO.Http11.Context;
using System.Net.Security;

public class BuilderTests
{
    [Fact]
    public void Builder_InitializesWithHandlerFactory()
    {
        var builder = new Builder<WiredHttp11, Http11Context>(() => new WiredHttp11(null!), [SslApplicationProtocol.Http11]);
        Assert.NotNull(builder.App);
        Assert.NotNull(builder.Services);
    }
}
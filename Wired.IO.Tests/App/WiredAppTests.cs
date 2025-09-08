using Xunit;
using Wired.IO.App;
using Wired.IO.Http11;
using Wired.IO.Http11.Context;
using Wired.IO.HttpExpress;
using System.Net.Security;

public class WiredAppTests
{
    [Fact]
    public void CreateExpressBuilder_ReturnsConfiguredBuilder()
    {
        var builder = WiredApp.CreateExpressBuilder();
        Assert.NotNull(builder);
        Assert.IsType<WiredHttpExpress<HttpExpressContext>>(builder.App.HttpHandler);
    }

    [Fact]
    public void CreateBuilder_Default_ReturnsConfiguredBuilder()
    {
        var builder = WiredApp.CreateBuilder();
        Assert.NotNull(builder);
        Assert.IsType<WiredHttp11>(builder.App.HttpHandler);
    }
}
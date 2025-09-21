using Wired.IO.App;
using Wired.IO.Http11;
using Wired.IO.Http11Express;

public class WiredAppTests
{
    [Fact]
    public void CreateExpressBuilder_ReturnsConfiguredBuilder()
    {
        var builder = WiredApp.CreateExpressBuilder();
        Assert.NotNull(builder);
        Assert.IsType<WiredHttp11Express<Http11ExpressContext>>(builder.App.HttpHandler);
    }

    [Fact]
    public void CreateBuilder_Default_ReturnsConfiguredBuilder()
    {
        var builder = WiredApp.CreateBuilder();
        Assert.NotNull(builder);
        Assert.IsType<WiredHttp11>(builder.App.HttpHandler);
    }
}
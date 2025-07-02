using System.Net.Security;
using Wired.IO.Builder;
using Wired.IO.Http11;
using Wired.IO.Http11.Context;
using Wired.IO.Http11.Middleware;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;

namespace Wired.IO.App;

public sealed class App
{
    public static Builder<WiredHttp11<Http11Context>, Http11Context> CreateBuilder()
    {
        return CreateBuilder([SslApplicationProtocol.Http11]);
    }

    public static Builder<WiredHttp11<Http11Context>, Http11Context> CreateBuilder(
        List<SslApplicationProtocol> sslApplicationProtocols)
    {
        var builder = new Builder<WiredHttp11<Http11Context>, Http11Context>(() =>
            new WiredHttp11<Http11Context>(
                new Http11HandlerArgs(
                    false,
                    null!,
                    null!)), sslApplicationProtocols);

        builder.UseResponse();
        builder.ReadRequestBody();

        return builder;
    }

    public static Builder<WiredHttp11<Http11Context>, Http11Context> CreateBuilder(
        Func<WiredHttp11<Http11Context>> handlerFactory)
    {
        return CreateBuilder(handlerFactory, [SslApplicationProtocol.Http11]);
    }

    public static Builder<WiredHttp11<Http11Context>, Http11Context> CreateBuilder(
        Func<WiredHttp11<Http11Context>> handlerFactory,
        List<SslApplicationProtocol> sslApplicationProtocols)
    {
        var builder = new Builder<WiredHttp11<Http11Context>, Http11Context>(
            handlerFactory,
            sslApplicationProtocols);

        builder.UseResponse();
        builder.ReadRequestBody();

        return builder;
    }

    public static Builder<THandler, TContext> CreateBuilder<THandler, TContext>(Func<THandler> handlerFactory)
        where THandler : IHttpHandler<TContext>
        where TContext : IContext
    {
        return CreateBuilder<THandler, TContext>(handlerFactory, [SslApplicationProtocol.Http11]);
    }

    public static Builder<THandler, TContext> CreateBuilder<THandler, TContext>(
        Func<THandler> handlerFactory,
        List<SslApplicationProtocol> sslApplicationProtocols)
        where THandler : IHttpHandler<TContext>
        where TContext : IContext
    {
        return new Builder<THandler, TContext>(handlerFactory, sslApplicationProtocols);
    }
}
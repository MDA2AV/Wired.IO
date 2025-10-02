using System.Net.Security;
using Wired.IO.Builder;
using Wired.IO.Http11;
using Wired.IO.Http11.Context;
using Wired.IO.Http11.Middleware;
using Wired.IO.Http11Express;
using Wired.IO.Http11Express.Context;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

/// <summary>
/// Provides factory methods to create and configure a <see cref="Builder{THandler, TContext}"/> instance
/// for setting up a Wired.IO application.
/// </summary>
public sealed class WiredApp
{
    public static Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext> CreateExpressBuilder()
    {
        var builder = new Builder<WiredHttp11Express<Http11ExpressContext>, Http11ExpressContext>(() =>
            new WiredHttp11Express<Http11ExpressContext>(), [SslApplicationProtocol.Http11]);

        return builder;
    }

    /// <summary>
    /// Creates a default HTTP/1.1 builder with built-in middleware and a default <see cref="WiredHttp11{TContext}"/> handler.
    /// </summary>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> for HTTP/1.1 with default settings.</returns>
    public static Builder<WiredHttp11, Http11Context> CreateBuilder()
    {
        return CreateBuilder([SslApplicationProtocol.Http11]);
    }

    /// <summary>
    /// Creates a default HTTP/1.1 builder with built-in middleware and a default <see cref="WiredHttp11{TContext}"/> handler,
    /// using a custom list of supported ALPN protocols.
    /// </summary>
    /// <param name="sslApplicationProtocols">
    /// A list of supported <see cref="SslApplicationProtocol"/> values (e.g., <see cref="SslApplicationProtocol.Http11"/>).
    /// </param>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> instance.</returns>
    public static Builder<WiredHttp11, Http11Context> CreateBuilder(
        List<SslApplicationProtocol> sslApplicationProtocols)
    {
        var builder = new Builder<WiredHttp11, Http11Context>(() =>
            new WiredHttp11(
                new Http11HandlerArgs(
                    false,
                    null!,
                    null!)), sslApplicationProtocols);

        builder.Services.AddDefaultMiddleware<Http11Context>();

        return builder;
    }

    /// <summary>
    /// Creates a custom HTTP/1.1 builder using the provided handler factory and built-in middleware.
    /// </summary>
    /// <param name="handlerFactory">A factory delegate that produces a <see cref="WiredHttp11{TContext}"/> instance.</param>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> instance using the specified handler.</returns>
    public static Builder<WiredHttp11, Http11Context> CreateBuilder(
        Func<WiredHttp11> handlerFactory)
    {
        return CreateBuilder(handlerFactory, [SslApplicationProtocol.Http11]);
    }

    /// <summary>
    /// Creates a custom HTTP/1.1 builder using the provided handler factory and supported ALPN protocols.
    /// </summary>
    /// <param name="handlerFactory">A factory delegate that produces a <see cref="WiredHttp11{TContext}"/> instance.</param>
    /// <param name="sslApplicationProtocols">
    /// A list of supported <see cref="SslApplicationProtocol"/> values for ALPN negotiation.
    /// </param>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> instance.</returns>
    public static Builder<WiredHttp11, Http11Context> CreateBuilder(
        Func<WiredHttp11> handlerFactory,
        List<SslApplicationProtocol> sslApplicationProtocols)
    {
        var builder = new Builder<WiredHttp11, Http11Context>(
            handlerFactory,
            sslApplicationProtocols);

        builder.Services.AddDefaultMiddleware<Http11Context>();

        return builder;
    }

    /// <summary>
    /// Creates a generic <see cref="Builder{THandler, TContext}"/> for a custom handler and context type.
    /// </summary>
    /// <typeparam name="THandler">The custom HTTP handler type implementing <see cref="IHttpHandler{TContext}"/>.</typeparam>
    /// <typeparam name="TContext">The request context type implementing <see cref="IBaseContext{TRequest,TResponse}"/>.</typeparam>
    /// <param name="handlerFactory">A factory delegate that produces an instance of <typeparamref name="THandler"/>.</param>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> instance.</returns>
    public static Builder<THandler, TContext> CreateBuilder<THandler, TContext>(Func<THandler> handlerFactory)
        where THandler : IHttpHandler<TContext>
        where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    {
        return CreateBuilder<THandler, TContext>(handlerFactory, [SslApplicationProtocol.Http11]);
    }

    /// <summary>
    /// Creates a generic <see cref="Builder{THandler, TContext}"/> for a custom handler and context type,
    /// using a custom list of supported ALPN protocols.
    /// </summary>
    /// <typeparam name="THandler">The custom HTTP handler type implementing <see cref="IHttpHandler{TContext}"/>.</typeparam>
    /// <typeparam name="TContext">The request context type implementing <see cref="IBaseContext{TRequest,TResponse}"/>.</typeparam>
    /// <param name="handlerFactory">A factory delegate that produces an instance of <typeparamref name="THandler"/>.</param>
    /// <param name="sslApplicationProtocols">
    /// A list of supported <see cref="SslApplicationProtocol"/> values for ALPN negotiation.
    /// </param>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> instance.</returns>
    public static Builder<THandler, TContext> CreateBuilder<THandler, TContext>(
        Func<THandler> handlerFactory,
        List<SslApplicationProtocol> sslApplicationProtocols)
        where THandler : IHttpHandler<TContext>
        where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    {
        return new Builder<THandler, TContext>(handlerFactory, sslApplicationProtocols);
    }
}
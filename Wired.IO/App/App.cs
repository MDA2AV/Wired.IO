using System.Net.Security;
using Wired.IO.Builder;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.Transport;
using Wired.IO.Transport.Socket;
using Wired.IO.Transport.Socket.Http11Express;
using Wired.IO.Transport.Socket.Http11Express.Context;
using Wired.IO.Transport.Socket.Http11Express.StaticHandlers;

namespace Wired.IO.App;

/// <summary>
/// Provides factory methods to create and configure a <see cref="Builder{THandler, TContext}"/> instance
/// for setting up a Wired.IO application.
/// </summary>
public sealed class WiredApp
{
    public static Builder<WiredHttp11Express, Http11ExpressContext> CreateExpressBuilder()
    {
        var builder = new Builder<WiredHttp11Express, Http11ExpressContext>(() => new WiredHttp11Express(), 
            [SslApplicationProtocol.Http11], new SocketTransport<Http11ExpressContext>());
        
        return builder.MapFlowControl("NotFound", FlowControl.CreateEndpointNotFoundHandler());
    }
    
    // Overload for the case when user wants to use a context which is a super type of Http11ExpressContext
    public static Builder<WiredHttp11Express<TContext>, TContext> CreateExpressBuilder<TContext>()
        where TContext : Http11ExpressContext, new()
    {
        var builder = new Builder<WiredHttp11Express<TContext>, TContext>(() => new WiredHttp11Express<TContext>(), 
            [SslApplicationProtocol.Http11], new SocketTransport<TContext>());

        return builder.MapFlowControl("NotFound", FlowControl.CreateEndpointNotFoundHandler());
    }

    /// <summary>
    /// Creates a generic <see cref="Builder{THandler, TContext}"/> for a custom handler and context type.
    /// </summary>
    /// <typeparam name="THandler">The custom HTTP handler type implementing <see cref="IHttpHandler"/>.</typeparam>
    /// <typeparam name="TContext">The request context type implementing <see cref="IBaseContext{TRequest,TResponse}"/>.</typeparam>
    /// <param name="handlerFactory">A factory delegate that produces an instance of <typeparamref name="THandler"/>.</param>
    /// <param name="transport"></param>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> instance.</returns>
    public static Builder<THandler, TContext> CreateBuilder<THandler, TContext>(Func<THandler> handlerFactory, ITransport<TContext> transport)
        where THandler : IHttpHandler
        where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    {
        return CreateBuilder<THandler, TContext>(handlerFactory, [SslApplicationProtocol.Http11], transport);
    }

    /// <summary>
    /// Creates a generic <see cref="Builder{THandler, TContext}"/> for a custom handler and context type,
    /// using a custom list of supported ALPN protocols.
    /// </summary>
    /// <typeparam name="THandler">The custom HTTP handler type implementing <see cref="IHttpHandler"/>.</typeparam>
    /// <typeparam name="TContext">The request context type implementing <see cref="IBaseContext{TRequest,TResponse}"/>.</typeparam>
    /// <param name="handlerFactory">A factory delegate that produces an instance of <typeparamref name="THandler"/>.</param>
    /// <param name="sslApplicationProtocols">
    /// A list of supported <see cref="SslApplicationProtocol"/> values for ALPN negotiation.
    /// </param>
    /// <param name="transport"></param>
    /// <returns>A configured <see cref="Builder{THandler, TContext}"/> instance.</returns>
    public static Builder<THandler, TContext> CreateBuilder<THandler, TContext>(
        Func<THandler> handlerFactory,
        List<SslApplicationProtocol> sslApplicationProtocols,
        ITransport<TContext> transport)
        where THandler : IHttpHandler
        where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    {
        return new Builder<THandler, TContext>(handlerFactory, sslApplicationProtocols, transport);
    }
}
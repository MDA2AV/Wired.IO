using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Wired.IO.App;
using Wired.IO.Common.Attributes;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Mediator;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Scans the provided assembly for all implementations of 
    /// <see cref="IRequestHandler{TRequest, TResponse}"/> or <see cref="IRequestHandler{TRequest}"/> and registers them with scoped lifetime.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="assembly">The assembly to scan for handlers</param>
    /// <param name="app"></param>
    /// <returns>The updated service collection</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Manually handled dynamic registration of types.")]
    public static IServiceCollection AddHandlers<TContext>(this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Assembly assembly,
        WiredApp<TContext> app) where TContext : IBaseContext<Protocol.Request.IBaseRequest, IBaseResponse>
    {
        var handlerTypes = new List<(Type HandlerType, Type InterfaceType, RouteAttribute? RouteInfo)>();

        var handlerInterfaceType = typeof(IRequestHandler<,>);
        var handlerInterfaceTypeNoReturn = typeof(IRequestHandler<>);
        var contextHandlerInterfaceType = typeof(IContextHandler<>);

        foreach (var type in assembly.GetExportedTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            // Check if it implements one of the handler interfaces
            foreach (var interfaceType in type.GetInterfaces())
            {
                if (!interfaceType.IsGenericType)
                    continue;

                var genericDef = interfaceType.GetGenericTypeDefinition();

                if (genericDef == contextHandlerInterfaceType)
                {
                    var routeAttr = type.GetCustomAttribute<RouteAttribute>();
                    handlerTypes.Add((type, interfaceType, routeAttr));
                    break;
                }

                if (genericDef == handlerInterfaceType || 
                    genericDef == handlerInterfaceTypeNoReturn)
                {
                    handlerTypes.Add((type, interfaceType, null!));
                    break;
                }
            }
        }

        foreach (var (handlerType, interfaceType, routeAttr) in handlerTypes)
        {
            // Register how to resolve the endpoint
            if (routeAttr is not null)
            {
                app.EncodedRoutes[routeAttr.HttpMethod].Add(routeAttr.Route);

                var fullRoute = routeAttr.HttpMethod + '_' + routeAttr.Route;

                services.AddKeyedScoped<Func<TContext, Task>>(fullRoute,
                (sp, key) =>
                { 
                    return async (context) =>
                    {
                        var handler = sp.GetRequiredService<IRequestDispatcher<TContext>>();

                        await handler.Send(context, context.CancellationToken);
                    };
                });
            }

            // Register the endpoint handler itself
            services.AddScoped(interfaceType,
                sp => ActivatorUtilities.CreateInstance(sp, handlerType));
        }
        return services;
    }
}
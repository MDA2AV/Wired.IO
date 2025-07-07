using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using Wired.IO.App;
using Wired.IO.Common.Attributes;
using Wired.IO.Protocol;

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
        App<TContext> app) where TContext : IContext
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
            Console.WriteLine($"Registering handler: {handlerType.Name}");
            Console.WriteLine($"Interface: {interfaceType.Name}");

            // Register how to resolve the endpoint
            if (routeAttr is not null)
            {
                Console.WriteLine($"Route: [{routeAttr.HttpMethod}] {routeAttr.Route}");

                app.EncodedRoutes[routeAttr.HttpMethod].Add(routeAttr.Route);

                var fullRoute = routeAttr.HttpMethod + "_" + routeAttr.Route;

                app.Routes.Add(fullRoute);

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


    /*
    public static IServiceCollection AddHandlers2(this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Assembly assembly)
    {
        var handlerTypes = new List<(Type HandlerType, Type InterfaceType)>();

        var handlerInterfaceType = typeof(IRequestHandler<,>);
        var handlerInterfaceTypeNoReturn = typeof(IRequestHandler<>);

        // Get all concrete types from the assembly that aren't abstract or interfaces
        foreach (var type in assembly.GetExportedTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            // Skip instantiating open generics, but discover all types
            // including closed generic types like Handler<string, int>

            foreach (var interfaceType in type.GetInterfaces())
            {
                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == handlerInterfaceType)
                {
                    handlerTypes.Add((type, interfaceType));

                    break;
                }

                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == handlerInterfaceTypeNoReturn)
                {
                    handlerTypes.Add((type, handlerInterfaceTypeNoReturn));

                    break;
                }
            }
        }

        foreach (var (handlerType, interfaceType) in handlerTypes)
        {
            Console.WriteLine(handlerType);
            Console.WriteLine(interfaceType);

            services.AddScoped(interfaceType,
                sp => ActivatorUtilities.CreateInstance(sp, handlerType));
        }

        return services;
    }
    */
}
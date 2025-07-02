using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Wired.IO.Mediator;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Scans the provided assembly for all implementations of 
    /// <see cref="IRequestHandler{TRequest, TResponse}"/> or <see cref="IRequestHandler{TRequest}"/> and registers them with scoped lifetime.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="assembly">The assembly to scan for handlers</param>
    /// <returns>The updated service collection</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Manually handled dynamic registration of types.")]
    public static IServiceCollection AddHandlers(this IServiceCollection services,
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
            services.AddScoped(interfaceType,
                sp => ActivatorUtilities.CreateInstance(sp, handlerType));
        }

        return services;
    }
}
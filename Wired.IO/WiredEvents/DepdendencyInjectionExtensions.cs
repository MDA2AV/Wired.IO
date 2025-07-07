using Microsoft.Extensions.DependencyInjection;

namespace Wired.IO.WiredEvents;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registers a wired event handler and its interface for the specified event type.
    /// </summary>
    /// <typeparam name="TEvent">The type of the wired event to handle.</typeparam>
    /// <typeparam name="THandler">The concrete implementation of <see cref="IWiredEventHandler{TEvent}"/>.</typeparam>
    /// <param name="services">The service collection to add the handler to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.</returns>
    /// <remarks>
    /// This registers both the concrete type and its interface, allowing resolution by the event handler interface.
    /// </remarks>
    public static IServiceCollection AddWiredEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IWiredEvent
        where THandler : class, IWiredEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddScoped<IWiredEventHandler<TEvent>>(sp => sp.GetRequiredService<THandler>());

        return services;
    }

    /// <summary>
    /// Registers wired event dispatchers capable of handling single or multiple wired events.
    /// </summary>
    /// <param name="services">The service collection to add the dispatchers to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.</returns>
    /// <remarks>
    /// This registers two delegates:
    /// <list type="bullet">
    ///   <item>
    ///     <description><c>Func&lt;IWiredEvent, Task&gt;</c>: for dispatching a single wired event.</description>
    ///   </item>
    ///   <item>
    ///     <description><c>Func&lt;IEnumerable&lt;IWiredEvent&gt;, Task&gt;</c>: for dispatching a collection of wired events.</description>
    ///   </item>
    /// </list>
    /// Each delegate resolves the correct handler from the container and dispatches using <see cref="WiredEventDispatcher"/>.
    /// </remarks>
    public static IServiceCollection AddWiredEventDispatcher(this IServiceCollection services)
    {
        services.AddScoped<Func<IWiredEvent, Task>>(sp =>
        {
            return async (wiredEvent) =>
            {
                var eventType = wiredEvent.GetType();
                var handlerType = typeof(IWiredEventHandler<>).MakeGenericType(eventType);

                var handler = sp.GetRequiredService(handlerType);
                await WiredEventDispatcher.Dispatch(handler, wiredEvent);
            };
        });

        services.AddScoped<Func<IEnumerable<IWiredEvent>, Task>>(sp =>
        {
            return async (wiredEvents) =>
            {
                foreach (var wiredEvent in wiredEvents.ToList())
                {
                    var eventType = wiredEvent.GetType();
                    var handlerType = typeof(IWiredEventHandler<>).MakeGenericType(eventType);

                    var handler = sp.GetRequiredService(handlerType);
                    await WiredEventDispatcher.Dispatch(handler, wiredEvent);
                }
            };
        });

        return services;
    }
}
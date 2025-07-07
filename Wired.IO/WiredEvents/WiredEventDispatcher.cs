using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Wired.IO.WiredEvents;


/// <summary>
/// Provides a centralized mechanism for dispatching wired events to their appropriate handlers.
/// </summary>
/// <remarks>
/// This dispatcher uses reflection to create and cache strongly-typed delegates that invoke
/// the corresponding <c>HandleAsync</c> method on implementations of <see cref="IWiredEventHandler{TEvent}"/>.
/// Caching ensures the reflection cost occurs only once per event type.
/// </remarks>
public static class WiredEventDispatcher
{
    /// <summary>
    /// A cache of compiled delegates for invoking the appropriate wired event handler methods.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<object, IWiredEvent, Task>> HandlersCache = new();

    /// <summary>
    /// Dispatches the specified wired event to the given handler instance.
    /// </summary>
    /// <param name="handler">The wired event handler instance. This must implement <see cref="IWiredEventHandler{TEvent}"/> for the event type.</param>
    /// <param name="wiredEvent">The wired event instance to be handled.</param>
    /// <returns>A task that represents the asynchronous operation of handling the event.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the appropriate <c>HandleAsync</c> method cannot be resolved for the given event type.
    /// </exception>
    public static Task Dispatch(object handler, IWiredEvent wiredEvent)
    {
        var eventType = wiredEvent.GetType();
        var handlerDelegate = HandlersCache.GetOrAdd(eventType, CreateHandlerDelegate);
        return handlerDelegate(handler, wiredEvent);
    }

    /// <summary>
    /// Creates and compiles a delegate for invoking the <c>HandleAsync</c> method
    /// on a strongly-typed <see cref="IWiredEventHandler{TEvent}"/> implementation.
    /// </summary>
    /// <param name="eventType">The concrete type of the wired event.</param>
    /// <returns>A compiled delegate capable of invoking <c>HandleAsync</c> on the appropriate handler.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the <c>HandleAsync</c> method is not found on the resolved handler interface.
    /// </exception>
    private static Func<object, IWiredEvent, Task> CreateHandlerDelegate(Type eventType)
    {
        var handlerInterfaceType = typeof(IWiredEventHandler<>).MakeGenericType(eventType);
        var methodInfo = handlerInterfaceType.GetMethod("HandleAsync");

        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var eventParam = Expression.Parameter(typeof(IWiredEvent), "event");

        // Cast handler to the right type
        var castHandler = Expression.Convert(handlerParam, handlerInterfaceType);

        // Cast wiredEvent to the right type
        var castEvent = Expression.Convert(eventParam, eventType);

        // Call handler.HandleAsync((T)event)
        var call = Expression.Call(castHandler, methodInfo!, castEvent);

        var lambda = Expression.Lambda<Func<object, IWiredEvent, Task>>(call, handlerParam, eventParam);
        return lambda.Compile();
    }
}
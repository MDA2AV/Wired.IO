namespace Wired.IO.WiredEvents;

/// <summary>
/// Defines a handler for a specific type of wired event.
/// </summary>
/// <typeparam name="TEvent">The type of the wired event to handle.</typeparam>
/// <remarks>
/// Implementations of this interface contain the logic that should be executed 
/// in response to a wired event. Typically, these handlers reside in the application 
/// or infrastructure layers and may depend on services via constructor injection.
/// </remarks>
public interface IWiredEventHandler<in TEvent> where TEvent : IWiredEvent
{
    /// <summary>
    /// Handles the specified wired event asynchronously.
    /// </summary>
    /// <param name="wiredEvent">The wired event instance to handle.</param>
    /// <returns>A task that represents the asynchronous handling operation.</returns>
    Task HandleAsync(TEvent wiredEvent);
}

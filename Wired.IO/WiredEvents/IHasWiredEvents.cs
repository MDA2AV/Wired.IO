namespace Wired.IO.WiredEvents;

/// <summary>
/// Represents an IContext that produces wired events.
/// </summary>
/// <remarks>
/// This interface is typically implemented by IContext to expose wired events
/// that have occurred as a result of business logic execution. These events can be
/// collected and dispatched after the unit of work is completed.
/// </remarks>
public interface IHasWiredEvents
{
    /// <summary>
    /// Gets the collection of wired events.
    /// </summary>
    /// <remarks>
    /// This list contains events that have occurred during the current operation or transaction
    /// and are pending dispatch.
    /// </remarks>
    IReadOnlyList<IWiredEvent> WiredEvents { get; }

    /// <summary>
    /// Adds a wired event to the current context's event queue.
    /// </summary>
    /// <param name="wiredEvent">The event instance to enqueue.</param>
    void AddWiredEvent(IWiredEvent wiredEvent);

    /// <summary>
    /// Clears all wired events.
    /// </summary>
    /// <remarks>
    /// This is typically called after wired events have been dispatched,
    /// to ensure the entity does not raise the same events again.
    /// </remarks>
    void ClearWiredEvents();
}

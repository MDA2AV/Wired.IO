namespace Wired.IO.WiredEvents;

/// <summary>
/// Represents a wired event that captures something significant that has occurred.
/// </summary>
/// <remarks>
/// Wired events are immutable messages that indicate a state change or important business event within the endpoint.
/// They are typically raised by aggregates and handled asynchronously to trigger side effects or workflows.
/// </remarks>
public interface IWiredEvent
{
    /// <summary>
    /// Gets the unique identifier of the wired event.
    /// </summary>
    /// <remarks>
    /// This ID is useful for tracking and deduplication purposes.
    /// </remarks>
    Guid Id { get; }

    /// <summary>
    /// Gets the UTC timestamp indicating when the event occurred.
    /// </summary>
    /// <remarks>
    /// This helps with ordering and auditing of wired activity over time.
    /// </remarks>
    DateTime OccurredOn { get; }
}
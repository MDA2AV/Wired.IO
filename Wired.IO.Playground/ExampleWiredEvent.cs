using Microsoft.Extensions.Logging;
using Wired.IO.WiredEvents;

namespace Wired.IO.Playground;

public class ExampleWiredEvent(string description) : IWiredEvent
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public string Description { get; } = description;
}

public class ExampleWiredEventHandler(ILogger<ExampleWiredEventHandler> logger) : IWiredEventHandler<ExampleWiredEvent>
{
    public async Task HandleAsync(ExampleWiredEvent wiredEvent)
    {
        // Handle the wired event here
        logger.LogInformation($"Handled wired event: {wiredEvent.Description} at {wiredEvent.OccurredOn}");
    }
}

public class Entity : IHasWiredEvents
{
    private readonly List<IWiredEvent> _wiredEvents = new();
    public IReadOnlyList<IWiredEvent> WiredEvents => _wiredEvents;
    public void AddWiredEvent(IWiredEvent wiredEvent) => _wiredEvents.Add(wiredEvent);
    public void ClearWiredEvents() => _wiredEvents.Clear();

    public void DoSomething()
    {
        // Simulate some operation that generates a wired event
        // For example, storing it in a database
        var wiredEvent = new ExampleWiredEvent("Entity did something important");
        AddWiredEvent(wiredEvent);
    }
}
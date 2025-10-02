using Wired.IO.Http11.Context;
using Moq;
using Wired.IO.WiredEvents;

public class Http11ContextTests
{
    [Fact]
    public void AddWiredEvent_AddsEvent()
    {
        var context = new Http11Context();
        var mockEvent = new Mock<IWiredEvent>().Object;
        context.AddWiredEvent(mockEvent);
        Assert.Contains(mockEvent, context.WiredEvents);
    }

    [Fact]
    public void ClearWiredEvents_RemovesAllEvents()
    {
        var context = new Http11Context();
        var mockEvent = new Mock<IWiredEvent>().Object;
        context.AddWiredEvent(mockEvent);
        context.ClearWiredEvents();
        Assert.Empty(context.WiredEvents);
    }
}
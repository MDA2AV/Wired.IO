using Microsoft.Extensions.Hosting;

namespace Wired.IO.App;

public sealed partial class App<TContext>
{
    public IHostBuilder HostBuilder { get; set; }

    public IHost InternalHost { get; set; } = null!;

    public async Task<App<TContext>> StartAsync()
    {
        await InternalHost.StartAsync();
        return this;
    }

    public async Task RunAsync()
    {
        await InternalHost.RunAsync();
    }

    public App<TContext> Start()
    {
        InternalHost.Start();
        return this;
    }

    public void Run()
    {
        InternalHost.Run();
    }
}

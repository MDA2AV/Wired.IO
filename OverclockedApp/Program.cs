using Wired.IO.App;

// dotnet publish -f net10.0 -c Release /p:PublishAot=true /p:OptimizationPreference=Speed

await WiredApp
    .CreateOverclockedBuilder()  // io_uring
    .NoScopedEndpoints()
    .UseRootEndpoints()
    .Port(8080)
    .MapGet("/json", context =>
    {

    })
    .Build()
    .RunAsync();
    
    
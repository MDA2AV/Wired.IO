using System.Buffers;
using Wired.IO.App;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var builder = App.CreateBuilder();

        var app = builder
            .Port(5000)
            .MapGet("/route", scope => async context =>
            {
                context
                    .Writer.Write("HTTP/1.1 200 OK\r\n"u8);
                context
                    .Writer.Write("Content-Length:0\r\n"u8);
                context
                    .Writer.Write("Content-Type: application/json\r\nConnection: keep-alive\r\n\r\n"u8);

                await context.Writer.FlushAsync();
            })
            .Build();

        await app.RunAsync();
    }
}

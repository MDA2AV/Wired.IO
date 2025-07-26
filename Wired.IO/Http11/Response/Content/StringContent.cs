using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

/// <summary>
/// Represents plain UTF-8 string response content.
/// </summary>
/// <param name="data">The string to write to the response body.</param>
public class StringContent(string data) : IResponseContent
{
    public ulong? Length { get; } = null!;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public async Task WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoding.UTF8.GetBytes(data));

        writer.Finish();

        await writer.FlushAsync();
    }

    public async Task WriteAsync(PipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoding.UTF8.GetBytes(data));

        await writer.FlushAsync();
    }
}
using System.Buffers;
using System.IO.Pipelines;
using Wired.IO.Http11.Response.Content;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Playground;

public class CustomResponseContent(ReadOnlyMemory<byte> data) : IResponseContent
{
    public ulong? Length => (ulong)data.Length;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public async Task WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        writer.Write(data.Span);
        await writer.FlushAsync();

        writer.Write("Additional information"u8);

        writer.Finish();
        await writer.FlushAsync();
    }

    public async Task WriteAsync(PipeWriter writer, uint bufferSize)
    {
        writer.Write(data.Span);

        await writer.FlushAsync();
    }
}
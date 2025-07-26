using System.Buffers;
using System.IO.Pipelines;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

/// <summary>
/// Represents raw binary response content backed by a <see cref="ReadOnlyMemory{Byte}"/> buffer.
/// </summary>
/// <param name="data">The binary data to be written to the response body.</param>
public sealed class RawContent(ReadOnlyMemory<byte> data) : IResponseContent
{
    public ulong? Length { get; } = (ulong)data.Length;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public async Task WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        writer.Write(data.Span);

        writer.Finish();

        await writer.FlushAsync();
    }

    public async Task WriteAsync(PipeWriter writer, uint bufferSize)
    {
        writer.Write(data.Span);

        await writer.FlushAsync();
    }
}

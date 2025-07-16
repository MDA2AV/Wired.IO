using System.IO.Pipelines;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

public sealed class RawContent(ReadOnlyMemory<byte> data) : IResponseContent
{
    public ulong? Length { get; } = (ulong)data.Length;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public ValueTask WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        throw new NotImplementedException();
    }

    public ValueTask WriteAsync(PipeWriter writer, uint bufferSize)
    {
        throw new NotImplementedException();
    }
}

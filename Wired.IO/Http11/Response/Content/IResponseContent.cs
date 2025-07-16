using System.IO.Pipelines;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

public interface IResponseContent
{
    ulong? Length { get; }

    ValueTask<ulong?> CalculateChecksumAsync();

    ValueTask WriteAsync(ChunkedPipeWriter writer, uint bufferSize);

    ValueTask WriteAsync(PipeWriter writer, uint bufferSize);
}
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

public interface IResponseContent
{
    ulong? Length { get; }

    ValueTask<ulong?> CalculateChecksumAsync();

    void Write(ChunkedPipeWriter writer, uint bufferSize);

    void Write(PlainPipeWriter writer, uint bufferSize);
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

public sealed class RawContent(ReadOnlyMemory<byte> data) : IResponseContent
{
    public ulong? Length { get; } = (ulong)data.Length;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public void Write(ChunkedPipeWriter writer, uint bufferSize)
    {
        throw new NotImplementedException();
    }

    public void Write(PlainPipeWriter writer, uint bufferSize)
    {
        throw new NotImplementedException();
    }
}

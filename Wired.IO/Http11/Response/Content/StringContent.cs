using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

public class StringContent(string data) : IResponseContent
{
    public ulong? Length { get; } = null!;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public void Write(ChunkedPipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoding.UTF8.GetBytes(data));
    }

    public void Write(PlainPipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoding.UTF8.GetBytes(data));
    }
}
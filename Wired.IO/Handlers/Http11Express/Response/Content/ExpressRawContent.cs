using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Wired.IO.Handlers.Http11Express.Response.Content;

[SkipLocalsInit]
public class ExpressRawContent : IExpressResponseContent
{
    private ReadOnlyMemory<byte>  _data;
    public ulong? Length { get; }

    public ExpressRawContent(ReadOnlyMemory<byte> data)
    {
        _data = data; 
        Length = (ulong)data.Length;
    }
    
    public void Write(PipeWriter writer)
    {
        writer.Write(_data.Span);
    }
}
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Express.Response.Content;

[SkipLocalsInit]
public class ExpressStringContent : IExpressResponseContent
{
    private string _data;
    public ulong? Length { get; private set; }

    public ExpressStringContent(string data)
    {
        _data = data; 
        Length = (ulong)data.Length;
    }
    
    public ExpressStringContent(string data, ulong? length = null)
    {
        _data = data;
        Length = length;
    }
    
    public void Set(string data)
    {
        Length = (ulong)data.Length;
        _data = data;
    }
    
    public void Write(PipeWriter writer)
    {
        writer.Write(Encoders.Utf8Encoder.GetBytes(_data));
    }
}
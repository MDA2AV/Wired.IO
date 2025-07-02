using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

public sealed class JsonContent(object data, JsonSerializerOptions options) : IResponseContent
{
    public ulong? Length => null;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public void Write(ChunkedPipeWriter writer, uint bufferSize)
    {
        var json = JsonSerializer.Serialize(data, options);

        writer.Write(Encoding.UTF8.GetBytes(json));
    }

    public void Write(PlainPipeWriter writer, uint bufferSize)
    {
        var json = JsonSerializer.Serialize(data, options);

        writer.Write(Encoding.UTF8.GetBytes(json));
    }
}

public sealed class JsonContent<T> : IResponseContent
    where T : notnull
{
    public JsonContent(T data, JsonSerializerOptions options)
    {
        _data = data;

        _serializedData = JsonSerializer.Serialize(data);

        Length = (ulong)_serializedData.Length;
    }

    private T _data;

    private readonly string _serializedData;

    public ulong? Length { get; set; }

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)_data.GetHashCode());

    public void Write(ChunkedPipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoding.UTF8.GetBytes(_serializedData));
    }

    public void Write(PlainPipeWriter writer, uint bufferSize)
    {
        throw new NotImplementedException();
    }
}
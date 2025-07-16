using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Response.Content;

public sealed class JsonContent(object data, JsonSerializerOptions options) : IResponseContent
{
    public ulong? Length => null;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public async ValueTask WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        var json = JsonSerializer.Serialize(data, options);

        writer.Write(Encoding.UTF8.GetBytes(json));

        writer.Finish();

        await writer.FlushAsync();
    }

    public async ValueTask WriteAsync(PipeWriter writer, uint bufferSize)
    {
        var json = JsonSerializer.Serialize(data, options);

        writer.Write(Encoding.UTF8.GetBytes(json));

        await writer.FlushAsync();
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

    public async ValueTask WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoding.UTF8.GetBytes(_serializedData));

        writer.Finish();

        await writer.FlushAsync();
    }

    public async ValueTask WriteAsync(PipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoding.UTF8.GetBytes(_serializedData));

        await writer.FlushAsync();
    }
}
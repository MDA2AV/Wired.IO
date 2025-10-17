using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Wired.IO.Protocol.Writers;
using Wired.IO.Utilities;

namespace Wired.IO.Http11.Response.Content;

/// <summary>
/// Represents JSON-encoded response content using <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
/// <param name="data">The object to serialize as JSON.</param>
/// <param name="options">The serializer options used to format the JSON output.</param>
public sealed class JsonContent(object data, JsonSerializerOptions options) : IResponseContent
{
    public ulong? Length => null;

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)data.GetHashCode());

    public async Task WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        var json = JsonSerializer.Serialize(data, options);

        writer.Write(Encoders.Utf8Encoder.GetBytes(json));

        writer.Finish();

        await writer.FlushAsync();
    }

    public async Task WriteAsync(PipeWriter writer, uint bufferSize)
    {
        var json = JsonSerializer.Serialize(data, options);

        writer.Write(Encoders.Utf8Encoder.GetBytes(json));

        await writer.FlushAsync();
    }
}

/// <summary>
/// Represents strongly-typed JSON-encoded response content with pre-serialized data and known length.
/// </summary>
/// <typeparam name="T">The type of the object to serialize. Must be non-null.</typeparam>
public sealed class JsonContent<T> : IResponseContent
    where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonContent{T}"/> class and pre-serializes the data.
    /// </summary>
    /// <param name="data">The object to serialize to JSON.</param>
    /// <param name="options">The serializer options to use (not applied in current implementation).</param>
    public JsonContent(T data, JsonSerializerOptions options)
    {
        _data = data;

        _serializedData = JsonSerializer.Serialize(data, options);

        Length = (ulong)_serializedData.Length;
    }

    private T _data;

    private readonly string _serializedData;

    public ulong? Length { get; set; }

    public ValueTask<ulong?> CalculateChecksumAsync() => new((ulong)_data.GetHashCode());

    public async Task WriteAsync(ChunkedPipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoders.Utf8Encoder.GetBytes(_serializedData));

        writer.Finish();

        await writer.FlushAsync();
    }

    public async Task WriteAsync(PipeWriter writer, uint bufferSize)
    {
        writer.Write(Encoders.Utf8Encoder.GetBytes(_serializedData));

        await writer.FlushAsync();
    }
}
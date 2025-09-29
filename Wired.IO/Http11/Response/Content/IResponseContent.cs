using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.ObjectPool;
using Wired.IO.Protocol.Writers;
using Wired.IO.Utilities;

namespace Wired.IO.Http11.Response.Content;

/// <summary>
/// Represents the body content of an HTTP response.
/// </summary>
/// <remarks>
/// This abstraction allows for efficient and flexible serialization of response bodies,
/// including optional length reporting, check summing, and streaming support for both
/// chunked and non-chunked transfer modes.
/// </remarks>
public interface IResponseContent
{
    /// <summary>
    /// Gets the length of the content in bytes, if known.
    /// </summary>
    /// <remarks>
    /// If <c>null</c>, the server will typically use chunked transfer encoding.
    /// </remarks>
    ulong? Length { get; }

    /// <summary>
    /// Computes a checksum of the content asynchronously, if supported.
    /// </summary>
    /// <returns>A <see cref="ulong"/> representing the checksum, or <c>null</c> if not applicable.</returns>
    ValueTask<ulong?> CalculateChecksumAsync();

    /// <summary>
    /// Writes the content to the response using a <see cref="ChunkedPipeWriter"/>, applying chunked transfer encoding.
    /// </summary>
    /// <param name="writer">The chunked writer used to send the response body.</param>
    /// <param name="bufferSize">The buffer size to use during writing operations.</param>
    Task WriteAsync(ChunkedPipeWriter writer, uint bufferSize);

    /// <summary>
    /// Writes the content to the response using a standard <see cref="PipeWriter"/>, typically with a known content length.
    /// </summary>
    /// <param name="writer">The writer used to send the response body.</param>
    /// <param name="bufferSize">The buffer size to use during writing operations.</param>
    Task WriteAsync(PipeWriter writer, uint bufferSize);
}


public interface IExpressResponseContent
{
    ulong? Length { get; }

    void Write(PipeWriter writer);
}

public interface IExpressResponseContent<TSerializable> : IExpressResponseContent
{
    IExpressResponseContent<TSerializable> Set(TSerializable data, JsonTypeInfo<TSerializable> typeInfo, ulong? length = null);
}

// Consider in IExpressResponseContent that its always chunked, there is really no easy way to know the content length for json
// For non json responses, might be a shot to have a known content length! This content length can be calculated in the IExpressResponseContent?
// This approach is likely always slower since an object has to be created every request. Maybe cache the IExpressResponseContent.. makes sense
// for repeated requests yes.

public static class Writers
{
    [ThreadStatic]
    public static Utf8JsonWriter? TWriter;

    public static readonly DefaultObjectPool<ChunkedWriter> ChunkedWriterPool
        = new(new ChunkedWriterObjectPolicy());

    private sealed class ChunkedWriterObjectPolicy : IPooledObjectPolicy<ChunkedWriter>
    {
        public ChunkedWriter Create() => new();

        public bool Return(ChunkedWriter writer)
        {
            writer.Reset();
            return true;
        }
    }
}

public class ExpressJsonContent(object data, ulong? length = null) : IExpressResponseContent
{
    public ulong? Length { get; } = length;

    //[ThreadStatic]
    //private static Utf8JsonWriter? _tWriter;

    public void Write(PipeWriter writer)
    {
        if (Length is not null)
        {

            Writers.TWriter ??= new Utf8JsonWriter(writer, new JsonWriterOptions { SkipValidation = true });
            Writers.TWriter.Reset(writer);

            JsonSerializer.Serialize(Writers.TWriter, data);

            return;
        }

        var chunkedWriter = Writers.ChunkedWriterPool.Get();
        chunkedWriter.SetOutput(writer);

        Writers.TWriter ??= new Utf8JsonWriter(chunkedWriter, new JsonWriterOptions { SkipValidation = true });
        Writers.TWriter.Reset(chunkedWriter);

        JsonSerializer.Serialize(Writers.TWriter, data);

        chunkedWriter.Complete();
    }
}

[SkipLocalsInit]
public class ExpressJsonContent3 : IExpressResponseContent
{
    private string _json;
    
    public ulong? Length { get; private set; }

    // Non-accessible
    private ExpressJsonContent3()
    {
        
    }

    public ExpressJsonContent3(string json)
    {
        Length = (ulong)json.Length;
        _json = json;
    }
    
    public ExpressJsonContent3 Set(string json)
    {
        Length = (ulong)json.Length;
        _json = json;

        return this;
    }
    
    public void Write(PipeWriter writer)
    {
        writer.Write(Encoders.Utf8Encoder.GetBytes(_json));
    }
}

[SkipLocalsInit]
public class ExpressJsonContent2<T> : IExpressResponseContent<T>
{
    [ThreadStatic]
    private static Utf8JsonWriter? _writer;

    private static readonly DefaultObjectPool<ChunkedWriter> ChunkedWriterPool
        = new(new ChunkedWriterObjectPolicy());

    private sealed class ChunkedWriterObjectPolicy : IPooledObjectPolicy<ChunkedWriter>
    {
        public ChunkedWriter Create() => new();

        public bool Return(ChunkedWriter writer)
        {
            writer.Reset();
            return true;
        }
    }

    private T _data;
    
    private JsonTypeInfo<T> _jsonTypeInfo;

    public ExpressJsonContent2<T> Set(T data, ulong? length = null)
    {
        Length = length;
        _data = data;

        return this;
    }
    
    public IExpressResponseContent<T> Set(T data, JsonTypeInfo<T> typeInfo, ulong? length = null)
    {
        Length = length;
        _data = data;
        _jsonTypeInfo = typeInfo;

        return this;
    }

    public ulong? Length { get; private set; }

    public void Write(PipeWriter writer)
    {
        if (Length is not null)
        {
            _writer ??= new Utf8JsonWriter(writer, new JsonWriterOptions { SkipValidation = true });
            _writer.Reset(writer);

            JsonSerializer.Serialize(_writer, _data);

            return;
        }

        var chunkedWriter = ChunkedWriterPool.Get();
        chunkedWriter.SetOutput(writer);

        _writer ??= new Utf8JsonWriter(chunkedWriter, new JsonWriterOptions { SkipValidation = true });
        _writer.Reset(chunkedWriter);
        
        JsonSerializer.Serialize(_writer, _data, _jsonTypeInfo);
        
        chunkedWriter.Complete();
        
        ChunkedWriterPool.Return(chunkedWriter);
    }
}
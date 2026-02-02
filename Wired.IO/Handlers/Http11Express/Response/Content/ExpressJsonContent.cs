using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.ObjectPool;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Handlers.Http11Express.Response.Content;

/// <summary>
/// Provides pooled writers and utilities for JSON serialization.
/// </summary>
public static class Writers
{
    /// <summary>
    /// A thread-static reusable <see cref="Utf8JsonWriter"/> instance.
    /// </summary>
    [ThreadStatic]
    public static Utf8JsonWriter? TWriter;

    /// <summary>
    /// A default object pool for <see cref="ChunkedWriter"/> instances.
    /// </summary>
    public static readonly DefaultObjectPool<ChunkedWriter> ChunkedWriterPool
        = new(new ChunkedWriterObjectPolicy());

    /// <summary>
    /// Provides pooling logic for <see cref="ChunkedWriter"/> objects.
    /// </summary>
    private sealed class ChunkedWriterObjectPolicy : IPooledObjectPolicy<ChunkedWriter>
    {
        /// <inheritdoc/>
        public ChunkedWriter Create() => new();

        /// <inheritdoc/>
        public bool Return(ChunkedWriter writer)
        {
            writer.Reset();
            return true;
        }
    }
}

/// <summary>
/// Represents JSON response content that serializes a strongly typed object.
/// </summary>
/// <typeparam name="T">The type of the object to serialize.</typeparam>
[SkipLocalsInit]
public class ExpressJsonContent<T> : IExpressResponseContent<T>
{
    private T _data;
    private JsonTypeInfo<T>? _jsonTypeInfo;

    /// <summary>
    /// Gets the length of the payload in bytes, if known.
    /// </summary>
    public ulong? Length { get; private set; }

    /// <summary>
    /// Initializes a new empty instance of <see cref="ExpressJsonContent{T}"/>.
    /// </summary>
#pragma warning disable CS8618
    internal ExpressJsonContent() { }
#pragma warning restore CS8618

    /// <summary>
    /// Initializes a new instance of <see cref="ExpressJsonContent{T}"/> with the given object.
    /// </summary>
    /// <param name="data">The data object to serialize as JSON.</param>
    /// <param name="typeInfo">Optional JSON type metadata to use for serialization.</param>
    /// <param name="length">Optional known length of the serialized payload.</param>
    public ExpressJsonContent(T data, JsonTypeInfo<T>? typeInfo = null, ulong? length = null)
    {
        Length = length;
        _data = data;
        _jsonTypeInfo = typeInfo;
    }

    /// <summary>
    /// Sets the payload object for this response.
    /// </summary>
    /// <param name="data">The object to serialize as JSON.</param>
    /// <param name="typeInfo">Optional JSON type metadata to use for serialization.</param>
    /// <param name="length">Optional known length of the serialized payload.</param>
    /// <returns>The current instance for chaining.</returns>
    public IExpressResponseContent<T> Set(T data, JsonTypeInfo<T>? typeInfo = null, ulong? length = null)
    {
        Length = length;
        _data = data;
        _jsonTypeInfo = typeInfo;

        return this;
    }

    /// <summary>
    /// Writes the serialized JSON payload to the given <see cref="PipeWriter"/>.
    /// Uses preallocated buffers and object pooling when length is unknown.
    /// </summary>
    /// <param name="writer">The pipe writer to write the JSON content to.</param>
    public void Write(PipeWriter writer)
    {
        if (Length is not null)
        {
            Writers.TWriter ??= new Utf8JsonWriter(writer, new JsonWriterOptions { SkipValidation = true });
            Writers.TWriter.Reset(writer);

            if (_jsonTypeInfo is null)
            {
                JsonSerializer.Serialize(Writers.TWriter, _data);
            }
            else
            {
                JsonSerializer.Serialize(Writers.TWriter, _data, _jsonTypeInfo);
            }

            return;
        }

        var chunkedWriter = Writers.ChunkedWriterPool.Get();
        chunkedWriter.SetOutput(writer);

        Writers.TWriter ??= new Utf8JsonWriter(chunkedWriter, new JsonWriterOptions { SkipValidation = true });
        Writers.TWriter.Reset(chunkedWriter);

        if (_jsonTypeInfo is null)
        {
            JsonSerializer.Serialize(Writers.TWriter, _data);
        }
        else
        {
            JsonSerializer.Serialize(Writers.TWriter, _data, _jsonTypeInfo);
        }

        chunkedWriter.Complete();
        Writers.ChunkedWriterPool.Return(chunkedWriter);
    }
}

/// <summary>
/// Represents JSON response content that serializes an arbitrary object without generics.
/// </summary>
/// <param name="data">The object to serialize as JSON.</param>
/// <param name="length">Optional known length of the serialized payload.</param>
public class ExpressJsonContent(object data, JsonSerializerOptions? jsonSerializerOptions = null, ulong? length = null) : IExpressResponseContent
{
    /// <summary>
    /// Gets the length of the payload in bytes, if known.
    /// </summary>
    public ulong? Length { get; } = length;

    /// <summary>
    /// Writes the serialized JSON payload to the given <see cref="PipeWriter"/>.
    /// Uses pooled <see cref="Utf8JsonWriter"/> and <see cref="ChunkedWriter"/> when needed.
    /// </summary>
    /// <param name="writer">The pipe writer to write the JSON content to.</param>
    public void Write(PipeWriter writer)
    {
        if (Length is not null)
        {
            Writers.TWriter ??= new Utf8JsonWriter(writer, new JsonWriterOptions { SkipValidation = true });
            Writers.TWriter.Reset(writer);

            JsonSerializer.Serialize(Writers.TWriter, data, jsonSerializerOptions);
            return;
        }

        var chunkedWriter = Writers.ChunkedWriterPool.Get();
        chunkedWriter.SetOutput(writer);

        Writers.TWriter ??= new Utf8JsonWriter(chunkedWriter, new JsonWriterOptions { SkipValidation = true });
        Writers.TWriter.Reset(chunkedWriter);

        JsonSerializer.Serialize(Writers.TWriter, data, jsonSerializerOptions);

        chunkedWriter.Complete();
    }
}

/// <summary>
/// Json serializer for AoT
/// </summary>
public class ExpressJsonAotContent(object data, JsonTypeInfo jsonTypeInfo, ulong? length = null) : IExpressResponseContent
{
    /// <summary>
    /// Gets the length of the payload in bytes, if known.
    /// </summary>
    public ulong? Length { get; } = length;

    /// <summary>
    /// Writes the serialized JSON payload to the given <see cref="PipeWriter"/>.
    /// Uses pooled <see cref="Utf8JsonWriter"/> and <see cref="ChunkedWriter"/> when needed.
    /// </summary>
    /// <param name="writer">The pipe writer to write the JSON content to.</param>
    public void Write(PipeWriter writer)
    {
        if (Length is not null)
        {
            Writers.TWriter ??= new Utf8JsonWriter(writer, new JsonWriterOptions { SkipValidation = true });
            Writers.TWriter.Reset(writer);

            JsonSerializer.Serialize(Writers.TWriter, data, jsonTypeInfo);
            return;
        }

        var chunkedWriter = Writers.ChunkedWriterPool.Get();
        chunkedWriter.SetOutput(writer);

        Writers.TWriter ??= new Utf8JsonWriter(chunkedWriter, new JsonWriterOptions { SkipValidation = true });
        Writers.TWriter.Reset(chunkedWriter);

        JsonSerializer.Serialize(Writers.TWriter, data, jsonTypeInfo);

        chunkedWriter.Complete();
    }
}
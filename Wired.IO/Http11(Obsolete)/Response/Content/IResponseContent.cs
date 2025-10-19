using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.ObjectPool;
using Microsoft.VisualBasic;
using Wired.IO.Http11Express.Response.Content;
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
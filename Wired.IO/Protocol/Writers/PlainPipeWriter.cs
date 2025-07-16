using System.Buffers;
using System.IO.Pipelines;

namespace Wired.IO.Protocol.Writers;

/// <summary>
/// A simple wrapper around <see cref="PipeWriter"/> that performs unbuffered, plain (non-chunked) writes.
/// </summary>
/// <remarks>
/// This writer is typically used when the content length is known ahead of time
/// and chunked transfer encoding is not required.
/// </remarks>
public sealed class PlainPipeWriter(PipeWriter writer)
{
    /// <summary>
    /// Gets the underlying <see cref="PipeWriter"/> used for writing the response.
    /// </summary>
    /// <returns>The internal <see cref="PipeWriter"/> instance.</returns>
    public PipeWriter GetPipeWriter() => writer;

    /// <summary>
    /// Writes the given buffer to the underlying pipe synchronously.
    /// </summary>
    /// <param name="buffer">A span of bytes to write to the pipe.</param>
    /// <remarks>
    /// If the buffer is empty, no data is written and the call is ignored.
    /// This method does not flush the pipe; the caller must flush explicitly.
    /// </remarks>
    public void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return;

        writer.Write(buffer);
    }

    public async ValueTask FlushAsync()
    {
        await writer.FlushAsync();
    }
}
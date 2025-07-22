/*MIT License

   Copyright (c) 2019-2024 Andreas Nägeli

   Permission is hereby granted, free of charge, to any person obtaining a copy
   of this software and associated documentation files (the "Software"), to deal
   in the Software without restriction, including without limitation the rights
   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
   copies of the Software, and to permit persons to whom the Software is
   furnished to do so, subject to the following conditions:

   The above copyright notice and this permission notice shall be included in all
   copies or substantial portions of the Software.

   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
   SOFTWARE.*/

using System.Buffers;

namespace Wired.IO.MemoryBuffers;

/// <summary>
/// An output stream using a pooled array to buffer small writes.
/// </summary>
/// <remarks>
/// Reduces write calls on underlying streams by collecting small writes
/// and flushing them only on request or if the internal buffer overflows.
/// Using a rented buffer from the array pool keeps allocations low.
/// Decreases the overhead of response content that issues a lot of small
/// writes (such as serializers or template renderers). As the content
/// length for such responses is typically not known beforehand, this
/// would cause all of those small writes to be converted into chunks, adding
/// a lot of communication overhead to the client connection.
/// </remarks>
public sealed class PoolBufferedStream : Stream
{
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Create(128 * 1024, 8192); // 1 GB

    #region Initialization

    public PoolBufferedStream(Stream stream, uint bufferSize)
    {
        Stream = stream;

        Buffer = Pool.Rent((int)bufferSize);
        Current = 0;
    }

    #endregion

    #region Disposing

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                Stream.Dispose();
            }
            finally
            {
                Pool.Return(Buffer);
            }
        }

        base.Dispose(disposing);
    }

    #endregion

    #region Get-/Setters

    public override bool CanRead => Stream.CanRead;

    public override bool CanSeek => Stream.CanSeek;

    public override bool CanWrite => Stream.CanWrite;

    public override long Length => Stream.Length;

    public override long Position
    {
        get => Stream.Position;
        set => Stream.Position = value;
    }

    private Stream Stream { get; }

    private byte[] Buffer { get; }

    private int Current { get; set; }

    #endregion

    #region Functionality

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Stream.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Stream.ReadAsync(buffer, cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => Stream.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => Stream.Read(buffer);

    public override int ReadByte() => Stream.ReadByte();

    public override long Seek(long offset, SeekOrigin origin) => Stream.Seek(offset, origin);

    public override void SetLength(long value) => Stream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count < (Buffer.Length - Current))
        {
            System.Buffer.BlockCopy(buffer, offset, Buffer, Current, count);

            Current += count;

            if (Current == Buffer.Length)
            {
                WriteBuffer();
            }
        }
        else
        {
            WriteBuffer();

            Stream.Write(buffer, offset, count);
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await WriteAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = buffer.Length;

        if (count < (Buffer.Length - Current))
        {
            buffer.CopyTo(Buffer.AsMemory()[Current..]);

            Current += count;

            if (Current == Buffer.Length)
            {
                await WriteBufferAsync(cancellationToken);
            }
        }
        else
        {
            await WriteBufferAsync(cancellationToken);

            await Stream.WriteAsync(buffer, cancellationToken);
        }
    }

    public override void Flush()
    {
        WriteBuffer();

        Stream.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await WriteBufferAsync(cancellationToken);

        await Stream.FlushAsync(cancellationToken);
    }

    private void WriteBuffer()
    {
        if (Current > 0)
        {
            Stream.Write(Buffer, 0, Current);

            Current = 0;
        }
    }

    private async ValueTask WriteBufferAsync(CancellationToken cancellationToken)
    {
        if (Current > 0)
        {
            await Stream.WriteAsync(Buffer.AsMemory(0, Current), cancellationToken);

            Current = 0;
        }
    }

    #endregion

}
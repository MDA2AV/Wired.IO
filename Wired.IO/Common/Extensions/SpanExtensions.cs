using System.Text;

namespace Wired.IO.Common.Extensions;

/// <summary>
/// Provides extension methods for efficiently writing UTF-8 and ASCII text into a <see cref="Span{Byte}"/> buffer.
/// </summary>
public static class SpanWriterExtensions
{
    /// <summary>
    /// Encodes the specified text as UTF-8 and writes it into the target <see cref="Span{Byte}"/> at the given offset.
    /// </summary>
    /// <param name="span">The destination byte span to write into.</param>
    /// <param name="offset">
    /// A reference to the offset index within the span. This value will be incremented by the number of bytes written.
    /// </param>
    /// <param name="text">The string to encode and write.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if the span is too small to contain the encoded text starting at the specified offset.
    /// </exception>
    public static void WriteUtf8(this Span<byte> span, ref int offset, string text)
    {
        var written = Encoding.UTF8.GetBytes(text.AsSpan(), span[offset..]);
        offset += written;
    }

    /// <summary>
    /// Encodes the specified text as ASCII and writes it into the target <see cref="Span{Byte}"/> at the given offset.
    /// </summary>
    /// <param name="span">The destination byte span to write into.</param>
    /// <param name="offset">
    /// A reference to the offset index within the span. This value will be incremented by the number of bytes written.
    /// </param>
    /// <param name="text">The string to encode and write.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if the span is too small to contain the encoded text starting at the specified offset,
    /// or if the string contains non-ASCII characters.
    /// </exception>
    public static void WriteAscii(this Span<byte> span, ref int offset, string text)
    {
        var written = Encoding.ASCII.GetBytes(text.AsSpan(), span[offset..]);
        offset += written;
    }
}
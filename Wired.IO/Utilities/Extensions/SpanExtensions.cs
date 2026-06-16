namespace Wired.IO.Utilities.Extensions;

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
    public static void WriteUtf8(this Span<byte> span, ref int offset, string text)
    {
        var written = Encoders.Utf8Encoder.GetBytes(text.AsSpan(), span[offset..]);
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
    public static void WriteAscii(this Span<byte> span, ref int offset, string text)
    {
        var written = Encoders.AsciiEncoder.GetBytes(text.AsSpan(), span[offset..]);
        offset += written;
    }
}
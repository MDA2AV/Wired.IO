using System.Text;

namespace Wired.IO.Common.Extensions;

public static class SpanWriterExtensions
{
    public static void WriteUtf8(this Span<byte> span, ref int offset, string text)
    {
        var written = Encoding.UTF8.GetBytes(text.AsSpan(), span[offset..]);

        offset += written;
    }

    public static void WriteAscii(this Span<byte> span, ref int offset, string text)
    {
        var written = Encoding.ASCII.GetBytes(text.AsSpan(), span[offset..]);

        offset += written;
    }
}
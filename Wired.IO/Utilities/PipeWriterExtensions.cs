using System.IO.Pipelines;
using System.Text;

namespace Wired.IO.Utilities;

public static class PipeWriterExtensions
{
    public static void WriteString(this PipeWriter writer, string text)
    {
        // Get the encoded bytes directly into the PipeWriter's buffer
        var span = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length));
        var bytesWritten = Encoding.UTF8.GetBytes(text, span);
        writer.Advance(bytesWritten);
    }
}
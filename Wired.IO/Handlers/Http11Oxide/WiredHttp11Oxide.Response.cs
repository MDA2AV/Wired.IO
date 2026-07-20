using System.Buffers.Text;
using ioxide;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Oxide;

public partial class WiredHttp11Oxide<TContext>
{
    // Status line + headers go straight into ioxide's write slab (Connection is the IBufferWriter).
    private static void WriteStatusLine(Connection conn, ResponseStatus status)
        => conn.Write(HttpStatusLines.Lines[(int)status]);

    private static void WriteHeaders(TContext ctx)
    {
        var conn = ctx.Connection;
        var resp = ctx.Response!;

        conn.Write("Server: W\r\n"u8);

        if (!resp.ContentType.IsEmpty)
        {
            conn.Write("Content-Type: "u8);
            conn.Write(resp.ContentType.AsSpan());
            conn.Write("\r\n"u8);
        }

        if (resp.ContentLength is { } cl)
        {
            conn.Write("Content-Length: "u8);
            Span<byte> num = stackalloc byte[20];
            Utf8Formatter.TryFormat(cl, num, out var n);
            conn.Write(num[..n]);
            conn.Write("\r\n"u8);
        }

        conn.Write(DateHelper.HeaderBytes); // "Date: …\r\n\r\n" — terminates the header block
    }

    private static void WriteSimple(Connection conn, ReadOnlySpan<byte> statusText)
    {
        conn.Write("HTTP/1.1 "u8);
        conn.Write(statusText);
        conn.Write("\r\nContent-Length: 0\r\n\r\n"u8);
    }
}

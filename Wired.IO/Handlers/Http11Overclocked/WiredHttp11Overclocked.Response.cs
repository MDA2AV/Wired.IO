using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Overclocked;

public partial class WiredHttp11Rocket<TContext>
{
    private static ReadOnlySpan<byte> ServerHeaderName => "Server: "u8;
    private static ReadOnlySpan<byte> ContentTypeHeader => "Content-Type: "u8;
    private static ReadOnlySpan<byte> ContentLengthHeader => "Content-Length: "u8;
    private static ReadOnlySpan<byte> ContentEncodingHeader => "Content-Encoding: "u8;
    private static ReadOnlySpan<byte> TransferEncodingHeader  => "Transfer-Encoding: "u8;
    private static ReadOnlySpan<byte> TransferEncodingChunkedHeader  => "Transfer-Encoding: chunked\r\n"u8;
    private static ReadOnlySpan<byte> LastModifiedHeader => "Last-Modified: "u8;
    private static ReadOnlySpan<byte> ExpiresHeader => "Expires: "u8;
    private static ReadOnlySpan<byte> ConnectionHeader => "Connection: "u8;
    private static ReadOnlySpan<byte> DateHeader => "Date: "u8;
    
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStatusLine(IBufferWriter<byte> writer, ResponseStatus statusCode) 
        => writer.Write(HttpStatusLines.Lines[(int)statusCode]);
    
    [SkipLocalsInit]
    private static void WriteHeaders(TContext context)
    {
        var writer = context.Connection;

        writer.Write("Server: W\r\n"u8);

        if (!context.Response!.ContentType.IsEmpty)
        {
            writer.Write(ContentTypeHeader);
            writer.Write(context.Response.ContentType.AsSpan());
            writer.Write("\r\n"u8);
        }

        // If ContentLength is not zero, its length is known and is valid to use Content-Length header
        if (context.Response.ContentLength is not null)
        {
            writer.Write(ContentLengthHeader);

            var buffer = writer.GetSpan(16); // 16 is enough for any int in UTF-8
            if (!Utf8Formatter.TryFormat((ulong)context.Response.ContentLength, buffer, out var written))
                throw new InvalidOperationException("Failed to format int");

            writer.Advance(written);

            writer.Write("\r\n"u8);
        }

        writer.Write(DateHelper.HeaderBytes);
    }
}
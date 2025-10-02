using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Http11Express;

public partial class WiredHttp11Express<TContext>
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
    
    private static void WriteResponse(TContext context)
    {
        WriteStatusLine(context.Writer, context.Response!.Status);

        WriteHeaders(context);

        WriteBody(context);
    }

    [SkipLocalsInit]
    private static void WriteBody(TContext context)
    {
        if (context.Response!.ContentLengthStrategy is ContentLengthStrategy.Action)
        {
            context.Response.Handler();
            return;
        }
        
        if (context.Response!.ContentLengthStrategy is ContentLengthStrategy.Utf8View)
        {
            context.Writer.Write(context.Response.Utf8Content.AsSpan());
            return;
        }

        context.Response.Content?.Write(context.Writer);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStatusLine(PipeWriter writer, ResponseStatus statusCode) 
        => writer.Write(HttpStatusLines.Lines[(int)statusCode]);
    
    [SkipLocalsInit]
    private static void WriteHeaders(TContext context)
    {
        var writer = context.Writer;

        writer.Write("Server: W\r\n"u8);

        if (!context.Response!.ContentType.IsEmpty)
        {
            writer.Write(ContentTypeHeader);
            writer.Write(context.Response.ContentType.AsSpan());
            writer.Write("\r\n"u8);
        }

        // TODO: Add Content Encoding header

        // If ContentLength is not zero, its length is known and is valid to use Content-Length header
        if (context.Response.ContentLength != 0)
        {
            writer.Write(ContentLengthHeader);

            var buffer = writer.GetSpan(16); // 16 is enough for any int in UTF-8
            if (!Utf8Formatter.TryFormat(context.Response.ContentLength, buffer, out var written))
                throw new InvalidOperationException("Failed to format int");

            writer.Advance(written);

            writer.Write("\r\n"u8);
        }
        else if (context.Response.ContentLengthStrategy is ContentLengthStrategy.Chunked or ContentLengthStrategy.Action)
        {
            writer.Write(TransferEncodingChunkedHeader);
        }

        /*foreach (var header in context.Response!.Utf8Headers)
        {

        }*/

        // TODO: Add Modified and Expires headers

        writer.Write(DateHelper.HeaderBytes);
    }
}

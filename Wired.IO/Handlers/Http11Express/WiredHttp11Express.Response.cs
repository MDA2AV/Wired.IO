using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Wired.IO.Handlers.Http11Express.Response;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.Handlers.Http11Express;

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
    
    private static async Task WriteResponse(TContext context)
    {
        WriteStatusLine(context.Writer, context.Response!.Status);

        WriteHeaders(context);

        await WriteBody(context);
    }

    [SkipLocalsInit]
    private static async ValueTask WriteBody(TContext context)
    {
        if (context.Response!.ContentLengthStrategy is ContentLengthStrategy.Action)
        {
            context.Response.Handler();
            return;
        }
        else if (context.Response!.ContentLengthStrategy is ContentLengthStrategy.AsyncTask)
        {
            await context.Response.AsyncHandler();
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
        else if (context.Response.ContentLengthStrategy is ContentLengthStrategy.Chunked or ContentLengthStrategy.Action)
        {
            writer.Write(TransferEncodingChunkedHeader);
        }
        
        if (context.Response.Utf8Headers is not null)
        {
            foreach (var header in context.Response.Utf8Headers)
            {
                writer.Write(header.Key.AsSpan());
                writer.Write(": "u8);
                writer.Write(header.Value.AsSpan());
                writer.Write("\r\n"u8);
            }
        }

        if (context.Response.Headers.Count > 0)
        {
            foreach (var header in context.Response.Headers)
            {
                writer.WriteString(header.Key);
                writer.Write(": "u8);
                writer.WriteString(header.Value);
                writer.Write("\r\n"u8);
            }
        }

        // TODO: Add Modified and Expires headers

        writer.Write(DateHelper.HeaderBytes);
    }
}

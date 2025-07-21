using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Wired.IO.Http11.Request;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Utilities;

namespace Wired.IO.Http11;

public partial class WiredHttp11<TContext, TRequest>
{
    //TODO: Set a timeout?, if received request is malformed, this loop is stuck, maybe use the cancellation token source

#if NET9_0_OR_GREATER

    public static async Task<bool> ExtractHeadersAsync(TContext context)
    {
        var reader = context.Reader;

        while (true)
        {
            var result = await reader.ReadAsync(context.CancellationToken);
            var buffer = result.Buffer;
            if (buffer.Length == 0)
            {
                throw new IOException("Client disconnected");
            }

            if (PipeReaderUtilities.TryAdvanceTo(new SequenceReader<byte>(buffer), "\r\n\r\n"u8, out var position))
            {
                var headerBytes = buffer.Slice(0, position);

                // Decode directly into stack memory
                var byteLength = (int)headerBytes.Length;
                var byteSpan = byteLength <= 1024 ? stackalloc byte[byteLength] : new byte[byteLength];
                headerBytes.CopyTo(byteSpan);

                var charCount = Encoding.UTF8.GetCharCount(byteSpan);
                var charSpan = charCount <= 1024 ? stackalloc char[charCount] : new char[charCount];
                Encoding.UTF8.GetChars(byteSpan, charSpan);

                // Advance past the headers
                reader.AdvanceTo(position);

                // Parse headers

                var lineStart = 0;
                var isFirstLine = true;

                while (true)
                {
                    var lineEnd = charSpan[lineStart..].IndexOf("\r\n");

                    if (lineEnd == -1)
                        break;

                    var line = charSpan.Slice(lineStart, lineEnd);
                    lineStart += lineEnd + 2; // skip \r\n

                    var request = Unsafe.As<Http11Request>(context.Request);

                    if (isFirstLine)
                    {
                        request.Headers.TryAdd(":Request-Line", new string(line));
                        isFirstLine = false;
                        continue;
                    }

                    var colonIndex = line.IndexOf(':');
                    if (colonIndex == -1) continue;

                    var key = line[..colonIndex].Trim();
                    var value = line[(colonIndex + 1)..].Trim();

                    request.Headers.TryAdd(new string(key), new string(value));
                }

                return true;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
                return false;
        }
    }

#elif NET8_0

    public static async Task<bool> ExtractHeadersAsync(TContext context)
    {
        var reader = context.Reader;

        while (true)
        {
            var result = await reader.ReadAsync(context.CancellationToken);
            var buffer = result.Buffer;

            if (TryFindHeaderEnd(buffer, out var position))
            {
                var headerBytes = buffer.Slice(0, position);

                DecodeHeaders(context, headerBytes);

                reader.AdvanceTo(position);
                return true;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
                return false;
        }
    }

    private static bool TryFindHeaderEnd(ReadOnlySequence<byte> buffer, out SequencePosition position)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (PipeReaderUtilities.TryAdvanceTo(reader, "\r\n\r\n"u8, out position))
            return true;

        position = default;
        return false;
    }

    private static void DecodeHeaders(TContext context, ReadOnlySequence<byte> buffer)
    {
        // Copy bytes to byteSpan
        var byteLength = (int)buffer.Length;
        var byteSpan = byteLength <= 1024 ? stackalloc byte[byteLength] : new byte[byteLength];
        buffer.CopyTo(byteSpan);

        // Decode to charSpan
        var charCount = Encoding.UTF8.GetCharCount(byteSpan);
        var charSpan = charCount <= 1024 ? stackalloc char[charCount] : new char[charCount];
        Encoding.UTF8.GetChars(byteSpan, charSpan);

        // Parse headers

        var lineStart = 0;
        var isFirstLine = true;

        while (lineStart < charSpan.Length)
        {
            var lineEnd = charSpan[lineStart..].IndexOf("\r\n");
            if (lineEnd == -1)
                break;

            var line = charSpan.Slice(lineStart, lineEnd);
            lineStart += lineEnd + 2;

            var request = Unsafe.As<Http11Request>(context.Request);

            if (isFirstLine)
            {
                request.Headers.TryAdd(":Request-Line", new string(line));
                isFirstLine = false;
                continue;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            request.Headers.TryAdd(new string(key), new string(value));
        }
    }


#endif

    private static ConnectionType GetConnectionType(string connectionType)
    {
        return connectionType.ToLowerInvariant() switch
        {
            "keep-alive" => ConnectionType.KeepAlive,
            "close" => ConnectionType.Close,
            "websocket" => ConnectionType.Websocket,
            "upgrade" => ConnectionType.Websocket,
            _ => ConnectionType.KeepAlive
        };
    }

    private static string ToRawHeaderString(PooledDictionary<string, string> headers)
    {
        // Estimate capacity: 32 headers × 40 chars average
        var builder = new StringBuilder(2048);

        // Write request line first
        if (headers.TryGetValue(":Request-Line", out var requestLine))
        {
            builder.Append(requestLine).Append("\r\n");
        }

        foreach (var (key, value) in headers)
        {
            if (key == ":Request-Line") continue;
            builder.Append(key).Append(": ").Append(value).Append("\r\n");
        }

        // Terminate header block
        builder.Append("\r\n");

        return builder.ToString();
    }

    public static void ParseHttpRequestLine(string line, IRequest request)
    {
        ReadOnlySpan<char> span = line;

        var firstSpace = span.IndexOf(' ');
        if (firstSpace == -1)
            throw new InvalidOperationException("Invalid request line");

        var secondSpaceStart = firstSpace + 1;
        var secondSpaceRelative = span[secondSpaceStart..].IndexOf(' ');
        if (secondSpaceRelative == -1)
            throw new InvalidOperationException("Invalid request line");

        var secondSpace = secondSpaceStart + secondSpaceRelative;

        // Method
        request.HttpMethod = line[..firstSpace];

        // Path + Query
        var targetStart = firstSpace + 1;
        var targetLength = secondSpace - targetStart;
        var targetSpan = span.Slice(targetStart, targetLength);

        var queryStart = targetSpan.IndexOf('?');

        ReadOnlySpan<char> pathSpan;
        ReadOnlySpan<char> querySpan = default;

        if (queryStart == -1)
        {
            pathSpan = targetSpan;
        }
        else
        {
            pathSpan = targetSpan[..queryStart];
            querySpan = targetSpan[(queryStart + 1)..];
        }

        request.Route = line.Substring(targetStart, pathSpan.Length);

        if (querySpan.IsEmpty)
            return;

        var queryOffset = targetStart + queryStart + 1; // Offset from beginning of original line

        var current = 0;
        while (current < querySpan.Length)
        {
            var separator = querySpan[current..].IndexOf('&');
            ReadOnlySpan<char> pair;
            var pairStart = current;

            if (separator == -1)
            {
                pair = querySpan[current..];
                current = querySpan.Length;
            }
            else
            {
                pair = querySpan.Slice(current, separator);
                current += separator + 1;
            }

            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex == -1)
            {
                var key = pair.ToString(); // key without value
                request.QueryParameters?.TryAdd(key, ReadOnlyMemory<char>.Empty);
            }
            else
            {
                var key = pair[..equalsIndex].ToString();
                var valueStartInLine = queryOffset + pairStart + equalsIndex + 1;
                var valueLength = pair.Length - equalsIndex - 1;

                var valueMem = line.AsMemory(valueStartInLine, valueLength);
                request.QueryParameters?.TryAdd(key, valueMem);
            }
        }
    }
}
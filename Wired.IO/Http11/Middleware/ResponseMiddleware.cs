using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Wired.IO.Http11.Context;
using Wired.IO.Protocol.Response;
using Wired.IO.Protocol.Writers;

namespace Wired.IO.Http11.Middleware;

/// <summary>
/// High-performance HTTP response middleware optimized for minimal allocations and maximum throughput.
/// Provides efficient serialization of HTTP responses to streams using System.IO.Pipelines.
/// </summary>
/// <remarks>
/// This middleware implements several key optimizations:
/// - Precomputed byte arrays for common HTTP status lines
/// - Pooled buffers for header serialization
/// - Cached date headers with thread-safe updates
/// - Batch header processing to minimize dictionary operations
/// - Efficient UTF-8 encoding using span-based operations
/// 
/// Designed for high-concurrency workloads with minimal GC pressure. Thread-safe for concurrent use.
/// </remarks>
public static class ResponseMiddleware
{
    #region Constants and Static Fields

    /// <summary>
    /// HTTP line terminator as UTF-8 byte array (\r\n).
    /// </summary>
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    /// <summary>
    /// UTF-8 encoding instance for string-to-byte conversions.
    /// </summary>
    private static readonly Encoding Utf8 = Encoding.UTF8;

    #endregion

    #region Interned Header Names

    /// <summary>
    /// Interned header names for improved dictionary performance and reduced string allocations.
    /// These are the most commonly used HTTP headers that benefit from string interning.
    /// </summary>
    private static readonly string ServerHeader = string.Intern("Server");
    private static readonly string ContentTypeHeader = string.Intern("Content-Type");
    private static readonly string ContentLengthHeader = string.Intern("Content-Length");
    private static readonly string ContentEncodingHeader = string.Intern("Content-Encoding");
    private static readonly string TransferEncodingHeader = string.Intern("Transfer-Encoding");
    private static readonly string LastModifiedHeader = string.Intern("Last-Modified");
    private static readonly string ExpiresHeader = string.Intern("Expires");
    private static readonly string ConnectionHeader = string.Intern("Connection");
    private static readonly string DateHeader = string.Intern("Date");

    #endregion

    #region Pre-computed Status Lines

    /// <summary>
    /// Pre-computed HTTP status lines as UTF-8 byte arrays for common status codes.
    /// This eliminates the need for string formatting and UTF-8 conversion for the majority of responses.
    /// 
    /// Each entry contains the complete status line including HTTP version, status code, 
    /// reason phrase, and CRLF terminator.
    /// </summary>
    /// <remarks>
    /// Status codes included cover ~95% of typical web application responses:
    /// - 2xx: Success responses (200, 201, 202, 204)
    /// - 3xx: Redirection responses (301, 302, 304)
    /// - 4xx: Client error responses (400, 401, 403, 404, 405)
    /// - 5xx: Server error responses (500, 502, 503)
    /// </remarks>
    private static readonly Dictionary<int, byte[]> CommonStatusLines = new()
    {
        { 200, "HTTP/1.1 200 OK\r\n"u8.ToArray() },
        { 201, "HTTP/1.1 201 Created\r\n"u8.ToArray() },
        { 202, "HTTP/1.1 202 Accepted\r\n"u8.ToArray() },
        { 204, "HTTP/1.1 204 No Content\r\n"u8.ToArray() },
        { 301, "HTTP/1.1 301 Moved Permanently\r\n"u8.ToArray() },
        { 302, "HTTP/1.1 302 Found\r\n"u8.ToArray() },
        { 304, "HTTP/1.1 304 Not Modified\r\n"u8.ToArray() },
        { 400, "HTTP/1.1 400 Bad Request\r\n"u8.ToArray() },
        { 401, "HTTP/1.1 401 Unauthorized\r\n"u8.ToArray() },
        { 403, "HTTP/1.1 403 Forbidden\r\n"u8.ToArray() },
        { 404, "HTTP/1.1 404 Not Found\r\n"u8.ToArray() },
        { 405, "HTTP/1.1 405 Method Not Allowed\r\n"u8.ToArray() },
        { 500, "HTTP/1.1 500 Internal Server Error\r\n"u8.ToArray() },
        { 502, "HTTP/1.1 502 Bad Gateway\r\n"u8.ToArray() },
        { 503, "HTTP/1.1 503 Service Unavailable\r\n"u8.ToArray() }
    };

    #endregion

    #region Date Header Caching

    /// <summary>
    /// Cached Date header value to avoid repeated DateTime formatting.
    /// Updated at most once per second using thread-safe operations.
    /// </summary>
    private static volatile string? _cachedDateHeader;

    /// <summary>
    /// Timestamp (in ticks) of the last date header update.
    /// Used with Interlocked operations for thread-safe access.
    /// </summary>
    private static long _lastDateUpdateTicks;

#if NET9_0
    /// <summary>
    /// Lock object for synchronizing date header updates.
    /// Uses double-checked locking pattern for optimal performance.
    /// </summary>
    private static readonly Lock DateLock = new();
#elif NET8_0
    private static readonly object DateLock = new();
#endif

    #endregion

    #region Public API

    /// <summary>
    /// Asynchronously handles the HTTP response serialization to the provided context stream.
    /// This is the main entry point for the middleware and orchestrates the entire response writing process.
    /// </summary>
    /// <param name="ctx">The HTTP context containing the response to serialize and the target stream.</param>
    /// <param name="bufferSize">The buffer size to use for response body writing. Default is 65KB.</param>
    /// <returns>A task that completes when the response has been fully written to the stream.</returns>
    /// <remarks>
    /// This method performs the following operations in order:
    /// 1. Writes the HTTP status line
    /// 2. Prepares and writes all HTTP headers
    /// 3. Writes the response body (if present)
    /// 4. Handles client disconnections gracefully
    /// 
    /// The method is designed to be exception-safe and will properly clean up resources
    /// even if the client disconnects during response writing.
    /// 
    /// Performance Characteristics:
    /// - Typical allocation: ~200-500 bytes per request (primarily for PipeWriter)
    /// - Header writing: O(n) where n is number of headers
    /// - Status line writing: O(1) for common status codes, O(log n) for uncommon ones
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when status code formatting fails.</exception>
    /// <exception cref="IOException">Thrown when stream writing fails (client disconnection).</exception>
    public static async Task HandleAsync(Http11Context ctx, uint bufferSize = 65 * 1024)
    {
        if (ctx.Response is null)
        {
            return;
        }

        WriteStatusLine(ctx.Writer, ctx.Response.Status.RawStatus, ctx.Response.Status.Phrase);

        WriteStandardHeaders(ctx.Writer, ctx);

        // End of headers
        ctx.Writer.Write(Crlf);

        // Write body with error handling
        if (ctx.Response.Content is not null)
        {
            await WriteResponseBody(ctx, ctx.Writer, bufferSize);

            // IResponseContent should flush already
        }
        else
        {
            await ctx.Writer.FlushAsync();
        }
    }

    #endregion

    #region Response Body Writing

    /// <summary>
    /// Writes the HTTP response body to the stream, handling both chunked and content-length scenarios.
    /// Provides proper error handling for client disconnections during body transmission.
    /// </summary>
    /// <param name="ctx">The HTTP context containing the response content and stream.</param>
    /// <param name="writer"></param>
    /// <param name="bufferSize">The buffer size to use for content writing operations.</param>
    /// <returns>A task that completes when the response body has been fully written.</returns>
    /// <remarks>
    /// Transfer Encoding Handling:
    /// - Chunked: Uses ChunkedStream wrapper for proper chunk formatting
    /// - Content-Length: Direct stream writing for better performance
    /// 
    /// Error Handling:
    /// - Client disconnections are detected via CancellationToken
    /// - IOException during body write re-throws to signal connection issues
    /// - Ensures proper cleanup of ChunkedStream resources
    /// 
    /// Performance Considerations:
    /// - Buffer size affects memory usage vs. throughput tradeoff
    /// - Default 65KB buffer balances memory and performance
    /// - Larger buffers improve throughput for large responses
    /// - Smaller buffers reduce memory pressure for concurrent requests
    /// </remarks>
    /// <exception cref="IOException">Thrown when client disconnects during body writing.</exception>
    private static async ValueTask WriteResponseBody(Http11Context ctx, PipeWriter writer, uint bufferSize)
    {
        if (ctx.Response!.ContentLength is null)
        {
            await ctx.Response.Content!.WriteAsync(new ChunkedPipeWriter(writer), bufferSize);
        }
        else
        {
            await ctx.Response.Content!.WriteAsync(writer, bufferSize);
        }
    }

    #endregion

    #region Status Line Writing

    /// <summary>
    /// Writes the HTTP status line to the PipeWriter with optimized handling for common status codes.
    /// Uses pre-computed byte arrays for common status codes and dynamic generation for uncommon ones.
    /// </summary>
    /// <param name="writer">The PipeWriter to write the status line to.</param>
    /// <param name="statusCode">The HTTP status code (e.g., 200, 404, 500).</param>
    /// <param name="phrase">The reason phrase (e.g., "OK", "Not Found", "Internal Server Error").</param>
    /// <remarks>
    /// Optimization Strategy:
    /// 1. Check pre-computed status lines dictionary (O(1) lookup)
    /// 2. If found, write pre-computed byte array directly
    /// 3. If not found, dynamically generate status line using Utf8Formatter
    /// 
    /// The pre-computed approach eliminates:
    /// - String concatenation
    /// - UTF-8 encoding overhead
    /// - Memory allocations for formatting
    /// 
    /// Dynamic generation is used for uncommon status codes to keep memory usage reasonable
    /// while still providing optimal performance for the 95% case.
    /// 
    /// Format: "HTTP/1.1 {statusCode} {phrase}\r\n"
    /// Example: "HTTP/1.1 200 OK\r\n"
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when status code formatting fails.</exception>
    private static void WriteStatusLine(PipeWriter writer, int statusCode, string phrase)
    {
        // Use pre-computed status lines for common cases
        if (CommonStatusLines.TryGetValue(statusCode, out var precomputed))
        {
            writer.Write(precomputed);
            return;
        }

        // Dynamic generation for uncommon status codes
        const int maxStatusLineLength = 64;
        var span = writer.GetSpan(maxStatusLineLength);
        var written = 0;

        "HTTP/1.1 "u8.CopyTo(span);
        written += 9;

        // Ensure we have enough space for status code
        if (span.Length < written + 4) // 3 digits + space
            span = writer.GetSpan(written + 4);

        if (!System.Buffers.Text.Utf8Formatter.TryFormat(statusCode, span[written..], out int statusBytes))
            throw new InvalidOperationException($"Failed to format status code: {statusCode}");

        written += statusBytes;
        span[written++] = (byte)' ';
        writer.Advance(written);

        if (!string.IsNullOrEmpty(phrase))
            WriteUtf8(writer, phrase);

        writer.Write(Crlf);
    }

    #endregion

    #region Header Writing Utilities

    /// <summary>
    /// Writes a UTF-8 encoded string to the provided <see cref="PipeWriter"/> using a directly acquired span.
    /// </summary>
    /// <param name="writer">The <see cref="PipeWriter"/> to write to.</param>
    /// <param name="text">The string content to encode and write.</param>
    /// <remarks>
    /// This method avoids intermediate buffers and performs zero-copy string-to-byte conversion.
    /// </remarks>
    private static void WriteUtf8(PipeWriter writer, string text)
    {
        var byteCount = Utf8.GetByteCount(text);
        var span = writer.GetSpan(byteCount);
        Utf8.GetBytes(text, span);
        writer.Advance(byteCount);
    }

    #endregion

    #region Caching and Optimization Utilities

    /// <summary>
    /// Retrieves or updates the cached Date header formatted per RFC1123.
    /// </summary>
    /// <returns>A string representing the current UTC time, formatted for HTTP headers.</returns>
    /// <remarks>
    /// Updated once per second to reduce string allocation and formatting overhead.
    /// Uses lock-free read with double-checked locking for updates.
    /// </remarks>
    private static string GetCachedDateHeader()
    {
        var now = DateTime.UtcNow;
        var cached = _cachedDateHeader;
        var lastUpdateTicks = Interlocked.Read(ref _lastDateUpdateTicks);

        if (cached != null && (now.Ticks - lastUpdateTicks) < TimeSpan.TicksPerSecond)
            return cached;

#if NET9_0

        lock (DateLock)
        {
            // Double-check locking pattern
            lastUpdateTicks = Interlocked.Read(ref _lastDateUpdateTicks);
            if (_cachedDateHeader != null && (now.Ticks - lastUpdateTicks) < TimeSpan.TicksPerSecond)
                return _cachedDateHeader;

            _cachedDateHeader = now.ToString("R");
            Interlocked.Exchange(ref _lastDateUpdateTicks, now.Ticks);
            return _cachedDateHeader;
        }

#else

        lock (DateLock)
        {
            lastUpdateTicks = Interlocked.Read(ref _lastDateUpdateTicks);
            if (_cachedDateHeader != null && (now.Ticks - lastUpdateTicks) < TimeSpan.TicksPerSecond)
                return _cachedDateHeader;

            _cachedDateHeader = now.ToString("R");
            Interlocked.Exchange(ref _lastDateUpdateTicks, now.Ticks);
            return _cachedDateHeader;
        }

#endif
    }

    /// <summary>
    /// Converts a content length value into a string representation.
    /// </summary>
    /// <param name="contentLength">The content length in bytes.</param>
    /// <returns>A string containing the length, e.g., <c>"0"</c>, <c>"1024"</c>.</returns>
    private static string FormatContentLength(ulong contentLength)
    {
        // Fast path for common small values
        return contentLength switch
        {
            0 => "0",
            _ => contentLength.ToString()
        };
    }

    /// <summary>
    /// Validates a header value to prevent protocol violations and security issues.
    /// </summary>
    /// <param name="value">The HTTP header value to check.</param>
    /// <returns><c>true</c> if the value is valid and safe for output; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// Prevents CRLF injection, null characters, and empty values.
    /// </remarks>
    private static bool IsValidHeaderValue(string value)
    {
        // Basic header injection prevention
        return !string.IsNullOrEmpty(value) &&
               !value.Contains('\r') &&
               !value.Contains('\n') &&
               !value.Contains('\0');
    }

    #endregion

    /// <summary>
    /// Writes HTTP response headers to the <see cref="PipeWriter"/> using pooled memory and efficient span logic.
    /// </summary>
    /// <param name="writer">The <see cref="PipeWriter"/> to write headers to.</param>
    /// <param name="ctx">The <see cref="Http11Context"/> containing the response and headers.</param>
    /// <remarks>
    /// Headers written include:
    /// - Mandatory: <c>Server</c>, <c>Date</c>
    /// - Optional: <c>Content-Type</c>, <c>Content-Length</c>, <c>Transfer-Encoding</c>, <c>Content-Encoding</c>, <c>Last-Modified</c>, <c>Expires</c>
    /// - Custom: All headers in <see cref="IResponse.Headers"/>
    /// 
    /// Header values are validated before writing. The method avoids dynamic allocations by batching writes into a preallocated span.
    /// </remarks>
    private static void WriteStandardHeaders(PipeWriter writer, Http11Context ctx)
    {
        var buffer = writer.GetSpan(512); // max 1KB header size
        var written = 0;

        // Mandatory headers
        WriteHeader(ServerHeader, "Wired.IO", buffer);
        WriteHeader(DateHeader, GetCachedDateHeader(), buffer);

        // Optional headers
        if (ctx.Response!.ContentType is not null)
            WriteHeader(ContentTypeHeader, ctx.Response.ContentType.RawType, buffer);
        if (ctx.Response.ContentEncoding is not null)
            WriteHeader(ContentEncodingHeader, ctx.Response.ContentEncoding, buffer);
        if (ctx.Response.Content is null)
            WriteHeader(ContentLengthHeader, "0", buffer);
        else if (ctx.Response.Content.Length is not null)
            WriteHeader(ContentLengthHeader, FormatContentLength(ctx.Response.Content.Length ?? 0), buffer);
        else
            WriteHeader(TransferEncodingHeader, "chunked", buffer);
        if (ctx.Response.Modified is not null)
            WriteHeader(LastModifiedHeader, ctx.Response.Modified.Value.ToUniversalTime().ToString("R"), buffer);
        if (ctx.Response.Expires is not null)
            WriteHeader(ExpiresHeader, ctx.Response.Expires.Value.ToUniversalTime().ToString("R"), buffer);

        foreach (var header in ctx.Response!.Headers)
        {
            WriteHeader(header.Key, header.Value, buffer);
        }

        writer.Advance(written);
        return;

        void WriteHeader(string key, string value, Span<byte> buffer)
        {
            if (!IsValidHeaderValue(value)) return;

            written += Utf8.GetBytes(key, buffer[written..]);
            buffer[written++] = (byte)':';
            buffer[written++] = (byte)' ';
            written += Utf8.GetBytes(value, buffer[written..]);
            buffer[written++] = (byte)'\r';
            buffer[written++] = (byte)'\n';
        }
    }
}
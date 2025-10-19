namespace Wired.IO.Utilities;

internal static class MimeTypes
{
    internal static ReadOnlySpan<byte> GetMimeType(string route)
    {
        // Use pattern matching to return the appropriate MIME type based on the extension
        return Path.GetExtension(route) switch
        {
            ".html" => "text/html"u8,               // HTML documents
            ".css" => "text/css"u8,                 // CSS stylesheets
            ".js" => "application/javascript"u8,    // JavaScript files
            ".json" => "application/json"u8,        // JSON data
            ".png" => "image/png"u8,                // PNG images
            ".jpg" => "image/jpeg"u8,               // JPEG images
            ".gif" => "image/gif"u8,                // GIF images
            _ => "application/octet-stream"u8,      // Default for unknown file types
        };
    }
}
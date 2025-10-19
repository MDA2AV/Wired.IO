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

    internal static ReadOnlySpan<byte> GetSpaMimeType(string route)
    {
        // Extract extension (null or empty if none)
        var ext = Path.GetExtension(route);

        if (string.IsNullOrEmpty(ext))
            return "text/html"u8; // SPA fallback: no extension → assume HTML

        return ext switch
        {
            // ===== HTML / Documents =====
            ".html" or ".htm" => "text/html"u8,
            ".xhtml" => "application/xhtml+xml"u8,
            ".xml" => "application/xml"u8,
            ".txt" => "text/plain"u8,
            ".md" => "text/markdown"u8,
            ".csv" => "text/csv"u8,
            ".tsv" => "text/tab-separated-values"u8,
            ".rtf" => "application/rtf"u8,
            ".pdf" => "application/pdf"u8,

            // ===== Web App / SPA Assets =====
            ".js" or ".mjs" or ".cjs" => "application/javascript"u8,
            ".jsx" or ".tsx" or ".ts" => "text/javascript"u8,
            ".json" => "application/json"u8,
            ".map" => "application/json"u8,
            ".css" => "text/css"u8,
            ".scss" or ".sass" => "text/x-scss"u8,
            ".less" => "text/x-less"u8,

            // ===== Images =====
            ".png" => "image/png"u8,
            ".jpg" or ".jpeg" => "image/jpeg"u8,
            ".jpe" => "image/jpeg"u8,
            ".gif" => "image/gif"u8,
            ".bmp" => "image/bmp"u8,
            ".ico" => "image/x-icon"u8,
            ".svg" => "image/svg+xml"u8,
            ".tif" or ".tiff" => "image/tiff"u8,
            ".webp" => "image/webp"u8,
            ".avif" => "image/avif"u8,
            ".heif" or ".heic" => "image/heic"u8,
            ".apng" => "image/apng"u8,

            // ===== Audio =====
            ".mp3" => "audio/mpeg"u8,
            ".wav" => "audio/wav"u8,
            ".ogg" or ".oga" => "audio/ogg"u8,
            ".m4a" => "audio/mp4"u8,
            ".aac" => "audio/aac"u8,
            ".flac" => "audio/flac"u8,
            ".weba" => "audio/webm"u8,
            ".mid" or ".midi" => "audio/midi"u8,

            // ===== Video =====
            ".mp4" => "video/mp4"u8,
            ".m4v" => "video/x-m4v"u8,
            ".webm" => "video/webm"u8,
            ".ogv" => "video/ogg"u8,
            ".mov" => "video/quicktime"u8,
            ".avi" => "video/x-msvideo"u8,
            ".mkv" => "video/x-matroska"u8,

            // ===== Fonts =====
            ".woff" => "font/woff"u8,
            ".woff2" => "font/woff2"u8,
            ".ttf" => "font/ttf"u8,
            ".otf" => "font/otf"u8,
            ".eot" => "application/vnd.ms-fontobject"u8,
            ".sfnt" => "font/sfnt"u8,

            // ===== Archives / Packages =====
            ".zip" => "application/zip"u8,
            ".tar" => "application/x-tar"u8,
            ".gz" => "application/gzip"u8,
            ".br" => "application/x-brotli"u8,
            ".7z" => "application/x-7z-compressed"u8,
            ".rar" => "application/vnd.rar"u8,
            ".xz" => "application/x-xz"u8,

            // ===== Web Manifest / Icons =====
            ".webmanifest" or ".manifest" => "application/manifest+json"u8,
            ".appcache" => "text/cache-manifest"u8,

            // ===== Data Formats =====
            ".yaml" or ".yml" => "application/yaml"u8,
            ".ini" => "text/plain"u8,
            ".env" => "text/plain"u8,
            ".config" => "application/xml"u8,

            // ===== Binary Data =====
            ".bin" => "application/octet-stream"u8,
            ".dat" => "application/octet-stream"u8,
            ".exe" => "application/vnd.microsoft.portable-executable"u8,
            ".dll" => "application/vnd.microsoft.portable-executable"u8,
            ".wasm" => "application/wasm"u8,

            // ===== Misc =====
            ".rss" => "application/rss+xml"u8,
            ".atom" => "application/atom+xml"u8,
            ".vtt" => "text/vtt"u8,
            ".srt" => "application/x-subrip"u8,
            ".apk" => "application/vnd.android.package-archive"u8,
            ".dmg" => "application/x-apple-diskimage"u8,
            ".msi" => "application/x-msdownload"u8,

            // ===== Default =====
            _ => "application/octet-stream"u8,
        };
    }
}
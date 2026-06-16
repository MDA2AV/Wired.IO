using Wired.IO.Utilities.StringCache;

namespace Wired.IO.Handlers;

/// <summary>
/// Caches commonly-seen strings (routes, header keys/values, methods) to avoid repeated allocations
/// and string interning during hot paths.
/// </summary>
/// <remarks>
/// Backed by custom fast hash caches sized for typical HTTP workloads. The pre-cached sets include common
/// request methods and frequently-present headers/values seen in benchmarks (e.g., TechEmpower Plaintext JSON).
/// </remarks>
internal static class CachedData
{
    /// <summary>Cache of parsed routes (path components of the URL).</summary>
    internal static readonly FastHashStringCache32 CachedRoutes = new FastHashStringCache32();
    /// <summary>Cache of query-string keys.</summary>
    internal static readonly FastHashStringCache32 CachedQueryKeys = new FastHashStringCache32();
    /// <summary>Pre-cached HTTP methods (8 common verbs).</summary>
    internal static readonly FastHashStringCache16 PreCachedHttpMethods = new FastHashStringCache16([
        "GET",
        "POST",
        "PUT",
        "DELETE",
        "PATCH",
        "HEAD",
        "OPTIONS",
        "TRACE"
    ], 8);
    /// <summary>Pre-cached header keys commonly present in requests.</summary>
    internal static readonly FastHashStringCache32 PreCachedHeaderKeys = new FastHashStringCache32([
        "Host",
        "User-Agent",
        "Cookie",
        "Accept",
        "Accept-Language",
        "Connection"
    ]);
    /// <summary>Pre-cached header values commonly seen on the wire.</summary>
    internal static readonly FastHashStringCache32 PreCachedHeaderValues = new FastHashStringCache32([
        "keep-alive",
        "server",
    ]);
}
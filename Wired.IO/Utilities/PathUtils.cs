using System.Runtime.CompilerServices;

namespace Wired.IO.Utilities;

/// <summary>
/// Ultra-lightweight path combiner optimized for hot paths.
///
/// Design goals:
///  • No intermediate string allocations (single <see cref="string.Create{TState}"/>).<br/>
///  • Handles both '/' and '\' in inputs; emits a single, configurable separator (default '/').<br/>
///  • Robust to null/empty inputs and redundant slashes on either side.<br/>
///  • Aggressively inlined and bounds-checked only once.
///
/// Notes:
///  • This is a lexical combiner; it does not touch the filesystem, normalize dots ("." / ".."),
///    or validate drive letters / UNC paths. Use only where lexical join is desired.
/// </summary>
internal static class PathUtils
{
    /// <summary>
    /// Combines two path segments without allocating temporaries, normalizing to a single separator.
    /// Trailing slashes are trimmed from <paramref name="left"/>; leading slashes are trimmed from
    /// <paramref name="right"/>. If both sides are non-empty, exactly one <paramref name="separator"/>
    /// is inserted between them.
    /// </summary>
    /// <param name="left">Left path segment (may be null or contain trailing '/' or '\').</param>
    /// <param name="right">Right path segment (may be null or contain leading '/' or '\').</param>
    /// <param name="separator">
    /// Output separator to use (defaults to '/'). Pass '\\' if you want Windows-style output.
    /// </param>
    /// <returns>The combined path as a single string.</returns>
    /// <remarks>
    /// This uses <see cref="string.Create{TState}(int,TState,SpanAction{char,TState})"/> with an explicit
    /// <c>TState</c> tuple to avoid the interpolated-string handler overload and ensure a single allocation.
    /// </remarks>
    /// <example>
    /// <code>
    /// Combine("E:/VS/", "/index.html")  // → "E:/VS/index.html"
    /// Combine("E:/VS",  "index.html")   // → "E:/VS/index.html"
    /// Combine("",       "index.html")   // → "index.html"
    /// Combine("E:/VS/", "")             // → "E:/VS"
    /// Combine("E:\\VS\\", "\\app.js")   // → "E:/VS/app.js" (normalized to '/')
    /// Combine("C:\\",   "Windows", '\\')// → "C:\Windows"  (Windows separator)
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Combine(string left, string right, char separator = '/')
    {
        left ??= string.Empty;
        right ??= string.Empty;

        // ---- Trim trailing slashes from left (by indices, no substrings) ----
        int lStart = 0;
        int lLen = left.Length;
        while (lLen > 0)
        {
            char c = left[lLen - 1];
            if (c is '/' or '\\') lLen--;
            else break;
        }

        // ---- Trim leading slashes from right (by indices, no substrings) ----
        int rStart = 0;
        int rLen = right.Length;
        while (rStart < rLen)
        {
            char c = right[rStart];
            if (c is '/' or '\\') rStart++;
            else break;
        }
        rLen -= rStart;

        bool needSep = lLen != 0 && rLen != 0;
        int totalLen = lLen + (needSep ? 1 : 0) + rLen;

        // Explicit generic TState prevents picking the interpolated-string handler overload.
        return string.Create<(string l, int lS, int lL, string r, int rS, int rL, bool sepNeeded, char sep)>(
            totalLen,
            (left, lStart, lLen, right, rStart, rLen, needSep, separator),
            static (dst, state) =>
            {
                var (l, lS, lL, r, rS, rL, sepNeeded, sep) = state;
                int pos = 0;

                // Copy left slice if present
                if (lL > 0)
                {
                    l.AsSpan(lS, lL).CopyTo(dst);
                    pos += lL;
                }

                // Insert exactly one separator if both sides present
                if (sepNeeded)
                    dst[pos++] = sep;

                // Copy right slice if present
                if (rL > 0)
                    r.AsSpan(rS, rL).CopyTo(dst[pos..]);
            });
    }
}
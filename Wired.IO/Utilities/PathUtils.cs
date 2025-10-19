using System.Runtime.CompilerServices;

namespace Wired.IO.Utilities;

internal static class PathUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Combine(string left, string right, char separator = '/')
    {
        left ??= string.Empty;
        right ??= string.Empty;

        // Trim trailing slashes from left (by indices)
        int lStart = 0;
        int lLen = left.Length;
        while (lLen > 0)
        {
            char c = left[lLen - 1];
            if (c == '/' || c == '\\') lLen--;
            else break;
        }

        // Trim leading slashes from right (by indices)
        int rStart = 0;
        int rLen = right.Length;
        while (rStart < rLen)
        {
            char c = right[rStart];
            if (c == '/' || c == '\\') rStart++;
            else break;
        }
        rLen -= rStart;

        bool needSep = lLen != 0 && rLen != 0;
        int totalLen = lLen + (needSep ? 1 : 0) + rLen;

        // Explicit generic to avoid the interpolated-string overload
        return string.Create<(string l, int lS, int lL, string r, int rS, int rL, bool sepNeeded, char sep)>(
            totalLen,
            (left, lStart, lLen, right, rStart, rLen, needSep, separator),
            static (dst, state) =>
            {
                var (l, lS, lL, r, rS, rL, sepNeeded, sep) = state;
                int pos = 0;

                if (lL > 0)
                {
                    l.AsSpan(lS, lL).CopyTo(dst);
                    pos += lL;
                }

                if (sepNeeded)
                    dst[pos++] = sep;

                if (rL > 0)
                    r.AsSpan(rS, rL).CopyTo(dst.Slice(pos));
            });
    }
}
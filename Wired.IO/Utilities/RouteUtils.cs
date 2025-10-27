using System.Runtime.CompilerServices;

namespace Wired.IO.Utilities;

internal static class RouteUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Normalize(string route)
    {
        if (string.IsNullOrWhiteSpace(route)) return "/";
        var r = route.Trim();
        if (r[0] != '/') r = "/" + r;
        while (r.Contains("//", StringComparison.Ordinal))
            r = r.Replace("//", "/", StringComparison.Ordinal);
        if (r.Length > 1 && r.EndsWith("/", StringComparison.Ordinal))
            r = r.TrimEnd('/');
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Combine(string left, string right)
    {
        if (string.IsNullOrEmpty(right)) return Normalize(left);
        if (string.IsNullOrEmpty(left)) return Normalize(right);

        var l = Normalize(left);
        var r = Normalize(right);
        if (l == "/") return r;
        if (r == "/") return l;
        return l + r;
    }
}
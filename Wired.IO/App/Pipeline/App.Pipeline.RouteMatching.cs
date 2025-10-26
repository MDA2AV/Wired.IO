using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Caches matched routes for previously seen paths to speed up route resolution.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string?> RouteMatchCache = new();

    /// <summary>
    /// Caches compiled regular expressions for each route pattern to avoid recompilation.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> RouteRegexCache = new();

    /// <summary>
    /// Matches a request route against a set of encoded patterns using cached regular expressions.
    /// If a match is found, the matching pattern is returned and cached.
    /// </summary>
    /// <param name="patterns">A set of registered route patterns for the current HTTP method.</param>
    /// <param name="input">The actual route string from the request.</param>
    /// <returns>
    /// The matching pattern if found; otherwise, <c>null</c>.
    /// </returns>
    private static string? MatchEndpoint(HashSet<string> patterns, string input)
    {
        if (RouteMatchCache.TryGetValue(input, out var cachedPattern))
            return cachedPattern;

        foreach (var pattern in patterns)
        {
            var regex = RouteRegexCache.GetOrAdd(pattern, static p =>
                new Regex(ConvertToRegex(p), RegexOptions.Compiled | RegexOptions.CultureInvariant));

            if (!regex.IsMatch(input)) continue;

            RouteMatchCache[input] = pattern;
            return pattern;
        }

        RouteMatchCache[input] = null;
        return null;
    }

    /// <summary>
    /// Converts a route pattern with placeholders (e.g., <c>/users/:id</c>) into a regular expression pattern.
    /// </summary>
    /// <param name="pattern">The route pattern containing optional placeholders (e.g., <c>:id</c>).</param>
    /// <returns>A regex string that matches the route with placeholders replaced by wildcards.</returns>
    private static string ConvertToRegex(string pattern)
    {
        var regexPattern = Regex.Replace(pattern, @":\w+", "[^/]+");
        return $"^{regexPattern}$";
    }
}
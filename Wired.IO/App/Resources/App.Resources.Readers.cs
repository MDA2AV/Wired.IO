using System.Reflection;
using System.Runtime.CompilerServices;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;
using Wired.IO.Utilities;

namespace Wired.IO.App;

/// <summary>
/// Core application that serves static and SPA resources from either the filesystem
/// or embedded resources, based on a base-route → location mapping.
///
/// Hot-path notes:
///  • <see cref="TryGetStaticResourceBaseRoute"/> scans known base routes and returns the first prefix match.
///  • <see cref="TryReadResource"/> resolves a concrete asset by relative path under the matched base route.
///  • <see cref="TryReadFallbackSpaResource"/> resolves SPA entry ("index.html") under the matched base route.
///
/// Consider precomputing:
///  • A trie/prefix-table for base routes if you have many.
///  • An embedded manifest { route → manifestName } map at startup to avoid per-request LINQ & string ops.
/// </summary>
public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Determines which configured static base route the given request <paramref name="route"/> falls under.
    /// Returns the first key for which <paramref name="route"/> starts with that key (ordinal).
    /// If none match, falls back to <c>"/"</c>.
    /// </summary>
    /// <remarks>
    /// Performance: iterates keys; for large sets consider storing keys ordered DESC by length
    /// or using a prefix-trie to ensure longest-prefix wins and fewer comparisons.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetStaticResourceBaseRoute(string route, out string baseRoute)
    {
        ReadOnlySpan<char> input = route;

        foreach (var key in StaticResourceRouteToLocation.Keys)
        {
            if (input.StartsWith(key.AsSpan(), StringComparison.Ordinal))
            {
                baseRoute = key;
                return true;
            }
        }

        baseRoute = string.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to read the SPA fallback document (typically <c>index.html</c>) for the
    /// base route inferred from <paramref name="route"/>.
    /// </summary>
    /// <param name="route">Request path used only to infer the base route.</param>
    /// <param name="resource">On success, the bytes for <c>index.html</c>.</param>
    /// <returns><c>true</c> if found and loaded; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// File system: reads <c>{location.Path}/{baseRoute}/index.html</c> (lexically combined).<br/>
    /// Embedded: searches the assembly manifest for a name ending with <c>{baseRoute}.index.html</c> (dots).
    /// </remarks>
    internal static bool TryReadFallbackSpaResource(string route, out ReadOnlyMemory<byte> resource)
    {
        var baseRouteFound = TryGetStaticResourceBaseRoute(route, out var baseRoute);
        if (!baseRouteFound)
        {
            resource = default;
            return false;
        }

        var location = StaticResourceRouteToLocation[baseRoute];

        // Build "{baseRoute}index.html" as the logical web path.
        var filePath = $"{baseRoute}index.html";

        // Filesystem location: read directly from disk if present.
        if (location.LocationType == LocationType.FileSystem)
        {
            // Combine the on-disk base path with the logical file path.
            var fullFilePath = PathUtils.Combine(location.Path, filePath);

            if (File.Exists(fullFilePath))
            {
                resource = File.ReadAllBytes(fullFilePath);
                return true;
            }
        }

        // Embedded resource location: resolve manifest resource name and read.
        if (location is { LocationType: LocationType.EmbeddedResource, Assembly: not null })
        {
            resource = default;
            var gotResource = GetEmbeddedResource(filePath, location, ref resource);

            return gotResource;
        }

        resource = default;
        return false;
    }

    /// <summary>
    /// Attempts to read the MPA fallback document (typically <c>index.html</c>) for the
    /// base route inferred from <paramref name="route"/>.
    /// </summary>
    /// <param name="route">Request path used only to infer the base route.</param>
    /// <param name="resource">On success, the bytes for <c>index.html</c>.</param>
    /// <returns><c>true</c> if found and loaded; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// File system: reads <c>{location.Path}/{baseRoute}/index.html</c> (lexically combined).<br/>
    /// Embedded: searches the assembly manifest for a name ending with <c>{baseRoute}.index.html</c> (dots).
    /// </remarks>
    internal static bool TryReadFallbackMpaResource(string route, out ReadOnlyMemory<byte> resource)
    {
        var baseRouteFound = TryGetStaticResourceBaseRoute(route, out var baseRoute);
        if (!baseRouteFound)
        {
            resource = default;
            return false;
        }

        var location = StaticResourceRouteToLocation[baseRoute];

        var filePath = PathUtils.Combine(route[(baseRoute.Length)..], "index.html");

        // Filesystem location: read directly from disk if present.
        if (location.LocationType == LocationType.FileSystem)
        {
            // Combine the on-disk base path with the logical file path.
            var fullFilePath = PathUtils.Combine(location.Path, filePath);

            if (File.Exists(fullFilePath))
            {
                resource = File.ReadAllBytes(fullFilePath);
                return true;
            }
        }

        // Embedded resource location: resolve manifest resource name and read.
        if (location is { LocationType: LocationType.EmbeddedResource, Assembly: not null })
        {
            resource = default;
            var gotResource = GetEmbeddedResource(filePath, location, ref resource);

            return gotResource;
        }

        resource = default;
        return false;
    }

    /// <summary>
    /// Attempts to read a concrete static asset addressed by <paramref name="route"/>.
    /// </summary>
    /// <param name="route">Full request route (e.g. "/assets/app.js").</param>
    /// <param name="resource">On success, the asset bytes.</param>
    /// <returns><c>true</c> if asset was found and loaded; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// The method:
    ///  1) Finds the base route prefix for <paramref name="route"/>.<br/>
    ///  2) Slices the remainder (relative path under base).<br/>
    ///  3) Loads from either filesystem or embedded resources.
    /// </remarks>
    internal static bool TryReadResource(string route, out ReadOnlyMemory<byte> resource)
    {
        var baseRouteFound = TryGetStaticResourceBaseRoute(route, out var baseRoute);
        if (!baseRouteFound)
        {
            resource = default;
            return false;
        }

        var location = StaticResourceRouteToLocation[baseRoute];

        // Slice off the base route, yielding a relative file path (e.g., "app.js" or "css/site.css").
        var filePath = route[(baseRoute.Length)..];

        // Filesystem: attempt direct disk read.
        if (location.LocationType == LocationType.FileSystem)
        {
            // Join physical base path with relative file path.
            var fullFilePath = PathUtils.Combine(location.Path, filePath);

            if (File.Exists(fullFilePath))
            {
                resource = File.ReadAllBytes(fullFilePath);
                return true;
            }
        }

        // Embedded resource: resolve and read from the assembly manifest.
        if (location is { LocationType: LocationType.EmbeddedResource, Assembly: not null })
        {
            resource = default;
            var gotResource = GetEmbeddedResource(filePath, location, ref resource);
            return gotResource;
        }

        resource = default;
        return false;
    }


    /// <summary>
    /// Attempts to load an embedded resource from the specified <see cref="Location"/> into a <see cref="ReadOnlyMemory{Byte}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method constructs the exact manifest resource name by combining the assembly name, the base embedded
    /// resource path (<paramref name="location.Path"/>), and the relative file path (<paramref name="filePath"/>).
    /// It applies the same normalization rules used by the C# compiler:
    /// hyphens ('-') in folder names are replaced with underscores ('_'), while file names preserve their hyphens.
    /// </para>
    /// <para>
    /// This avoids scanning all manifest resource names and directly requests the matching resource stream
    /// via <see cref="Assembly.GetManifestResourceStream(string)"/>. The stream is read into a single contiguous
    /// <see cref="byte"/> buffer and returned as a <see cref="ReadOnlyMemory{Byte}"/>. The caller may cache or reuse
    /// this memory as appropriate.
    /// </para>
    /// <para>
    /// For hot paths, consider caching the normalized base prefix or implementing a resource name dictionary
    /// to reduce per-call string concatenation costs.
    /// </para>
    /// </remarks>
    /// <param name="filePath">
    /// The relative file path within the base embedded resource folder (e.g. <c>"guides/v1/index.html"</c>).
    /// This path may contain directory separators ('/' or '\') and may begin with a leading slash.
    /// </param>
    /// <param name="location">
    /// A <see cref="Location"/> describing where the embedded resource resides, including its
    /// <see cref="Location.LocationType"/>, base path (e.g. <c>"Resources.Docs"</c>), and target <see cref="Assembly"/>.
    /// </param>
    /// <param name="resource">
    /// When successful, receives the resource contents as a <see cref="ReadOnlyMemory{Byte}"/>.
    /// On failure, the value is set to <see cref="ReadOnlyMemory{Byte}.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the resource was found and read successfully; otherwise, <see langword="false"/>.
    /// </returns>
    /// <example>
    /// <code>
    /// ReadOnlyMemory&lt;byte&gt; resource;
    /// if (GetEmbeddedResource("guides/v1/index.html", docsLocation, ref resource))
    /// {
    ///     // Use the resource bytes (e.g., write to HTTP response)
    /// }
    /// </code>
    /// </example>
    private static bool GetEmbeddedResource(string filePath, Location location, ref ReadOnlyMemory<byte> resource)
    {
        if (location is not { LocationType: LocationType.EmbeddedResource, Assembly: not null })
        {
            resource = default;
            return false;
        }

        var asm = location.Assembly;

        // Build the exact manifest name once
        var expected = BuildExactManifestName(asm, location.Path, filePath);

        // Direct lookup – no enumeration over all resources
        using var s = asm.GetManifestResourceStream(expected);
        if (s is null)
        {
            resource = default;
            return false;
        }

        // Read to ROM<byte> (allocation unavoidable unless you add your own pooling)
        var len = checked((int)s.Length);
        var buffer = GC.AllocateUninitializedArray<byte>(len);
        s.ReadExactly(buffer); // .NET 7+ (ReadExactly). For older: use s.Read in a loop.

        resource = buffer;
        return true;

        // ---- helpers ----

        static string BuildExactManifestName(Assembly assembly, string basePath, string relativeFile)
        {
            // basePath is already a relative *folder* inside the assembly (can be dotted or slashed).
            // We must create:  "<AsmName>.<basePathNormalized>.<relativeTailNormalized>"

            var basePrefix = NormalizeBasePrefix(assembly, basePath); // ends with '.'
            var tail = NormalizeRelativeManifestTail(relativeFile);

            return basePrefix + tail;
        }

        static string NormalizeBasePrefix(Assembly assembly, string basePath)
        {
            // Accept dotted ("Resources.Docs") or slashed ("/Resources/Docs")
            var p = basePath
                .Replace('\\', '/')
                .Trim('/')          // "/Resources/Docs" -> "Resources/Docs"
                .Replace('/', '.')  // "Resources/Docs"  -> "Resources.Docs"
                .Trim('.')          // guard
                .Replace('-', '_'); // folder '-' -> '_' (Roslyn behavior)

            return $"{assembly.GetName().Name}.{p}.";
        }

        static string NormalizeRelativeManifestTail(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            path = path.Replace('\\', '/').TrimStart('/');

            var lastSlash = path.LastIndexOf('/');
            if (lastSlash < 0)
            {
                // File only; keep hyphens in *file* names
                return path;
            }

            var dir = path[..lastSlash];
            var file = path[(lastSlash + 1)..]; // keep hyphens in file name

            // Folder segments: '-' -> '_' to match manifest naming
            var dirs = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < dirs.Length; i++)
                dirs[i] = dirs[i].Replace('-', '_');

            return $"{string.Join('.', dirs)}.{file}";
        }
    }
}
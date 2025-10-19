using System.Reflection;
using System.Runtime.CompilerServices;

namespace Wired.IO.Utilities;

/// <summary>
/// Provides high-performance utilities for loading embedded resources
/// (e.g. static files, SPA assets, configuration blobs) from an assembly.
///
/// These methods are used internally by <see cref="WiredApp{TContext}"/> during
/// static resource initialization and caching.
///
/// Features:
///  • Zero allocation when possible (pre-sized array via <see cref="Stream.Length"/>)  
///  • Read-only memory return type for immutability  
///  • Graceful fallback for non-seekable streams  
///  • Optional non-throwing variant (<see cref="TryReadBytes"/>)
/// </summary>
internal static class EmbeddedResourceUtils
{
    /// <summary>
    /// Reads an embedded resource as a <see cref="ReadOnlyMemory{T}"/> of bytes
    /// from the specified <see cref="Assembly"/> and manifest resource name.
    ///
    /// Typically used for preloading HTML, CSS, JS, or image assets compiled into
    /// the executable via <c>EmbeddedResource</c> entries.
    /// </summary>
    /// <param name="assembly">
    /// The assembly containing the embedded resource.  
    /// Usually <c>typeof(Program).Assembly</c> or <c>Assembly.GetExecutingAssembly()</c>.
    /// </param>
    /// <param name="manifestName">
    /// The full manifest resource name (e.g. "MyApp.Resources.index.html").
    /// </param>
    /// <returns>
    /// A read-only memory buffer containing the full contents of the embedded resource.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="assembly"/> or <paramref name="manifestName"/> is null or empty.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown if the resource stream could not be found in the assembly.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown if the resource exceeds <see cref="int.MaxValue"/> bytes in size.
    /// </exception>
    public static ReadOnlyMemory<byte> ReadBytes(Assembly assembly, string manifestName)
    {
        if (assembly is null)
            throw new ArgumentNullException(nameof(assembly));

        if (string.IsNullOrEmpty(manifestName))
            throw new ArgumentNullException(nameof(manifestName));

        // Attempt to open the resource stream
        using Stream? s = assembly.GetManifestResourceStream(manifestName);
        if (s is null)
            throw new FileNotFoundException($"Embedded resource not found: '{manifestName}'.");

        // Use direct buffer allocation for known-length streams (zero-copy)
        if (s.CanSeek)
        {
            long len64 = s.Length;
            if (len64 > int.MaxValue)
                throw new IOException($"Resource too large: {len64} bytes.");

            int len = (int)len64;

            // Allocate without zeroing for maximum performance
            byte[] buffer = GC.AllocateUninitializedArray<byte>(len);

            // Fully read stream into pre-allocated buffer
            s.ReadExactly(buffer);

            // Implicit conversion to ReadOnlyMemory<byte>
            return buffer;
        }

        // Fallback for non-seekable resource streams (rare)
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray(); // implicit ReadOnlyMemory<byte>
    }

    /// <summary>
    /// Attempts to read an embedded resource as bytes without throwing exceptions.
    ///
    /// This is useful for optional or environment-specific resources (e.g. localization bundles).
    /// </summary>
    /// <param name="assembly">The assembly containing the embedded resource.</param>
    /// <param name="manifestName">The full manifest resource name.</param>
    /// <param name="bytes">
    /// When successful, contains the loaded bytes as a read-only memory buffer.
    /// When unsuccessful, set to <see cref="ReadOnlyMemory{Byte}.Empty"/>.
    /// </param>
    /// <returns><see langword="true"/> if the resource was read successfully; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadBytes(Assembly assembly, string manifestName, out ReadOnlyMemory<byte> bytes)
    {
        try
        {
            bytes = ReadBytes(assembly, manifestName);
            return true;
        }
        catch
        {
            bytes = default;
            return false;
        }
    }
}
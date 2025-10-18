using System.Reflection;

namespace Wired.IO.Utilities;

internal static class EmbeddedResourceUtils
{
    public static ReadOnlyMemory<byte> ReadBytes(Assembly assembly, string manifestName)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrEmpty(manifestName)) throw new ArgumentNullException(nameof(manifestName));

        using Stream? s = assembly.GetManifestResourceStream(manifestName);
        if (s is null)
            throw new FileNotFoundException($"Embedded resource not found: '{manifestName}'.");

        if (s.CanSeek)
        {
            long len64 = s.Length;
            if (len64 > int.MaxValue)
                throw new IOException($"Resource too large: {len64} bytes.");

            int len = (int)len64;
            byte[] buffer = GC.AllocateUninitializedArray<byte>(len);
            s.ReadExactly(buffer);
            return buffer; // implicit ReadOnlyMemory<byte> via array
        }
        else
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray(); // implicit ReadOnlyMemory<byte>
        }
    }

    public static bool TryReadBytes(Assembly assembly, string manifestName, out ReadOnlyMemory<byte> bytes)
    {
        try { bytes = ReadBytes(assembly, manifestName); return true; }
        catch { bytes = default; return false; }
    }
}
namespace Wired.IO.Utilities.StringCache;

public sealed class FastHashStringCache64
{
    private readonly Dictionary<ulong, string> _map;

    public FastHashStringCache64(List<string>? preCacheableStrings, int capacity = 256)
    {
        _map = new Dictionary<ulong, string>(capacity);

        if (preCacheableStrings is not null)
        {
            foreach (var preCacheableString in preCacheableStrings)
            {
                AddPredefined(preCacheableString);
            }
        }
    }

    public FastHashStringCache64(int capacity = 256)
    {
        _map = new Dictionary<ulong, string>(capacity);
    }

    public string GetOrAdd(ReadOnlySpan<byte> bytes)
    {
        ulong h = Fnva64(bytes);                // or xxHash64, etc.
        if (_map.TryGetValue(h, out var s))
            return s;                           // may be a false hit if collision

        s = Encoders.Utf8Encoder.GetString(bytes);     // alloc once per (colliding) hash
        _map[h] = s;                            // last-wins policy
        return s;
    }

    private void AddPredefined(string stringToCache)
    {
        var bytes = Encoders.Utf8Encoder.GetBytes(stringToCache); // ASCII safe for methods
        ulong h = Fnva64(bytes);
        _map[h] = stringToCache;
    }

    // FNV-1a 64-bit
    private static ulong Fnva64(ReadOnlySpan<byte> data)
    {
        const ulong off = 14695981039346656037UL;  // Fixed
        const ulong prime = 1099511628211UL;
        ulong h = off;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= prime;
        }
        return h;
    }
}
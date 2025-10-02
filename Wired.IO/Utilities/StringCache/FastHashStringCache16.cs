namespace Wired.IO.Utilities.StringCache;

public sealed class FastHashStringCache16
{
    private readonly Dictionary<ushort, string> _map; // Changed from ulong

    public FastHashStringCache16(List<string>? preCacheableStrings, int capacity = 256)
    {
        _map = new Dictionary<ushort, string>(capacity); // Changed from ulong
        if (preCacheableStrings is not null)
        {
            foreach (var preCacheableString in preCacheableStrings)
            {
                AddPredefined(preCacheableString);
            }
        }
    }

    public FastHashStringCache16(int capacity = 256)
    {
        _map = new Dictionary<ushort, string>(capacity); // Changed from ulong
    }

    public string GetOrAdd(ReadOnlySpan<byte> bytes)
    {
        ushort h = Fnv1a16(bytes); // Changed from ulong
        if (_map.TryGetValue(h, out var s))
            return s;                           // may be a false hit if collision
        s = Encoders.Utf8Encoder.GetString(bytes);
        _map[h] = s;
        return s;
    }

    private void AddPredefined(string stringToCache)
    {
        var bytes = Encoders.Utf8Encoder.GetBytes(stringToCache);
        ushort h = Fnv1a16(bytes); // Changed from ulong
        _map[h] = stringToCache;
    }

    private static ushort Fnv1a16(ReadOnlySpan<byte> data)
    {
        const ushort offset = 0x811C; // 33084
        const ushort prime = 0x0101; // 257
        ushort h = offset;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= prime;
        }
        return h;
    }
}
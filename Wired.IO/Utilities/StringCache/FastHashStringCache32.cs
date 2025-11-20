using System.Collections.Concurrent;

namespace Wired.IO.Utilities.StringCache;

public sealed class FastHashStringCache32
{
    private readonly ConcurrentDictionary<uint, string> _map; // Changed from ulong
    
    private const int ConcurrencyLevel = 1;

    public FastHashStringCache32(List<string>? preCacheableStrings, int capacity = 256)
    {
        _map = new ConcurrentDictionary<uint, string>(ConcurrencyLevel, capacity); // Changed from ulong
        if (preCacheableStrings is not null)
        {
            foreach (var preCacheableString in preCacheableStrings)
            {
                AddPredefined(preCacheableString);
            }
        }
    }

    public FastHashStringCache32(int capacity = 256)
    {
        _map = new ConcurrentDictionary<uint, string>(ConcurrencyLevel, capacity); // Changed from ulong
    }

    public string GetOrAdd(ReadOnlySpan<byte> bytes)
    {
        uint h = Fnv1a32(bytes); // Changed from ulong
        if (_map.TryGetValue(h, out var s))
            return s;
        s = Encoders.Utf8Encoder.GetString(bytes);
        _map[h] = s;
        return s;
    }

    private void AddPredefined(string stringToCache)
    {
        var bytes = Encoders.Utf8Encoder.GetBytes(stringToCache);
        uint h = Fnv1a32(bytes); // Changed from ulong
        _map[h] = stringToCache;
    }

    private static uint Fnv1a32(ReadOnlySpan<byte> data)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        uint h = offset;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= prime;
        }
        return h;
    }
}
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wired.IO.Utilities;

// This is a time/bomb struct to avoid allocations on FromLiteral calls
// It should only be used with literals that are guaranteed to live forever
public readonly struct Utf8View : IEquatable<Utf8View>
{
    private readonly nint _ptr;
    private readonly int _length;

    public bool IsEmpty => _ptr == 0 || _length == 0;

    private Utf8View(nint ptr, int length)
    {
        _ptr = ptr;
        _length = length;
    }

    public static unsafe Utf8View FromLiteral(ReadOnlySpan<byte> literal)
    {
        ref byte b0 = ref MemoryMarshal.GetReference(literal);
        return new Utf8View((nint)Unsafe.AsPointer(ref b0), literal.Length);
    }

    public unsafe ReadOnlySpan<byte> AsSpan() => new((byte*)_ptr, _length);

    public bool Equals(Utf8View other)
    {
        return _ptr == other._ptr && _length == other._length;
    }

    public override bool Equals(object? obj)
    {
        return obj is Utf8View other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_ptr, _length);
    }
}
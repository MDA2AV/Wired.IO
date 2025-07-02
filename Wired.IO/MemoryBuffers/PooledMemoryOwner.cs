using System.Buffers;

namespace Wired.IO.MemoryBuffers;

/// <summary>
/// A custom implementation of <see cref="IMemoryOwner{T}"/> that uses an <see cref="ArrayPool{T}"/> 
/// to rent and manage memory buffers efficiently.
/// 
/// This class encapsulates a rented array from the pool and provides a <see cref="Memory{T}"/> representation
/// of the buffer. It ensures the rented buffer is properly returned to the pool when no longer in use, 
/// avoiding unnecessary memory allocations and garbage collection pressure.
/// </summary>
public sealed class PooledMemoryOwner : IMemoryOwner<byte>
{
    // Fields

    /// <summary>
    /// The shared pool from which the buffer was rented.
    /// This is used to return the buffer when the instance is disposed.
    /// </summary>
    private readonly ArrayPool<byte> _pool;

    /// <summary>
    /// The rented buffer. This is the actual array managed by this instance.
    /// It is returned to the pool when the object is disposed.
    /// </summary>
    /// <remarks>
    /// A `null` value indicates that the buffer has already been returned to the pool
    /// and the object should no longer be used.
    /// </remarks>
    private byte[]? _buffer;

    // Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="PooledMemoryOwner"/> class.
    /// </summary>
    /// <param name="buffer">
    /// The byte array rented from the <see cref="ArrayPool{T}"/>.
    /// This buffer will be wrapped and managed by this instance.
    /// </param>
    /// <param name="length">
    /// The length of the buffer to expose via <see cref="Memory{T}"/>.
    /// This length must not exceed the actual length of the rented buffer.
    /// </param>
    /// <param name="pool">
    /// The <see cref="ArrayPool{T}"/> instance from which the buffer was rented.
    /// This is required to return the buffer when the object is disposed.
    /// </param>
    public PooledMemoryOwner(byte[] buffer, int length, ArrayPool<byte> pool)
    {
        // Assign the rented buffer
        _buffer = buffer;

        // Wrap the buffer in a Memory<byte> for safe, slice-able memory access
        Memory = new Memory<byte>(_buffer, 0, length);

        // Store the reference to the pool for returning the buffer
        _pool = pool;
    }

    // Properties

    /// <summary>
    /// Gets the <see cref="Memory{T}"/> representation of the rented buffer.
    /// This provides access to the buffer in a safe, slice-able, and memory-efficient way.
    /// </summary>
    public Memory<byte> Memory { get; }

    // Dispose Method
    /// <summary>
    /// Releases the resources used by this instance, specifically returning the rented buffer
    /// to the <see cref="ArrayPool{T}"/> for reuse.
    /// </summary>
    /// <remarks>
    /// This method is idempotent, meaning it is safe to call multiple times. 
    /// Subsequent calls will have no effect once the buffer has been returned to the pool.
    /// </remarks>
    public void Dispose()
    {
        // Check if the buffer has already been returned
        if (_buffer == null)
        {
            return;
        }

        // Return the buffer to the pool
        _pool.Return(_buffer);

        // Mark the buffer as returned by setting it to null
        _buffer = null;
    }
}
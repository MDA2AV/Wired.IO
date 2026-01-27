using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.ObjectPool;
using URocket.Connection;
using URocket.Utils;
using URocket.Utils.UnmanagedMemoryManager;
using Wired.IO.Handlers.Http11Rocket.Context;
using Wired.IO.Transport.Rocket;

namespace Wired.IO.Handlers.Http11Rocket;

// *************************** WORK IN PROGRESS **********************************

// Public non-generic facade
public sealed class WiredHttp11Rocket : WiredHttp11Rocket<Http11RocketContext> { }

public partial class WiredHttp11Rocket<TContext> : IRocketHttpHandler<TContext>
    where TContext : Http11RocketContext, new()
{
    /// <summary>
    /// Parser state machine for the multi-segment path.
    /// </summary>
    private enum State
    {
        /// <summary>Reading header lines until a blank line (CRLF) is found.</summary>
        Headers,
        /// <summary>Headers complete; body (if any) would be processed next.</summary>
        Body
    }
    
    /// <summary>
    /// Object pool used to recycle request contexts for reduced allocations and improved performance.
    /// </summary>
    private static readonly ObjectPool<TContext> ContextPool =
        new DefaultObjectPool<TContext>(new PipelinedContextPoolPolicy(), 4096 * 4);

    /// <summary>
    /// Pool policy that defines how to create and reset pooled <typeparamref name="{TContext}"/> instances.
    /// </summary>
    private class PipelinedContextPoolPolicy : PooledObjectPolicy<TContext>
    {
        /// <summary>
        /// Creates a new instance of <typeparamref name="{TContext}"/>.
        /// </summary>
        public override TContext Create() => new();

        /// <summary>
        /// Resets the context before returning it to the pool.
        /// </summary>
        /// <param name="context">The context instance to return.</param>
        /// <returns><c>true</c> if the context can be reused; otherwise, <c>false</c>.</returns>
        public override bool Return(TContext context)
        {
            context.Clear(); // User-defined reset method to clean internal state.
            return true;
        }
    }
    
    private readonly unsafe byte* _inflightData;
    private int _inflightTail;
    private readonly int _length;

    public unsafe WiredHttp11Rocket()
    {
        _length = 1024 * 16;
        
        // Allocating an unmanaged byte slab to store inflight data
        _inflightData = (byte*)NativeMemory.AlignedAlloc((nuint)_length, 64);

        _inflightTail = 0;
    }
    
    public async Task HandleClientAsync(Connection connection, Func<TContext, Task> pipeline, CancellationToken stoppingToken)
    {
        // Rent a context object from the pool
        var context = ContextPool.Get();
        context.Connection = connection;
        
        try
        {
            await ProcessRequestsAsync(context, pipeline);
        }
        catch(Exception e)
        {
            Console.WriteLine(e.Message);
            // TODO Check the CancellationTokenSource Impl @ Express Handler
        }
        finally
        {
            unsafe { NativeMemory.AlignedFree(_inflightData); }
            // Return context to pool for reuse
            ContextPool.Return(context);
        }
    }
    
    private static async Task ProcessRequestsAsync2(TContext context, Func<TContext, Task> pipeline)
    {
        while (true)
        {
            var result = await context.Connection.ReadAsync();
            if (result.IsClosed)
                break;
            
            // Get all ring buffers data
            var rings = context.Connection.GetAllSnapshotRingsAsUnmanagedMemory(result);
            // Create a ReadOnlySequence<byte> to easily slice the data
            var sequence = rings.ToReadOnlySequence();
            
            // Process received data...
            
            context.Request.HttpMethod = "GET";
            context.Request.Route = "/route";
            await pipeline(context);
            
            // Return rings to the kernel
            foreach (var ring in rings)
                context.Connection.ReturnRing(ring.BufferId);
            
            // Write the response
            var msg =
                "HTTP/1.1 200 OK\r\nContent-Length: 13\r\nContent-Type: text/plain\r\n\r\nHello, World!"u8;

            // Building an UnmanagedMemoryManager wrapping the msg, this step has no data allocation
            // however msg must be fixed/pinned because the engine reactor's needs to pass a byte* to liburing
            unsafe
            {
                var unmanagedMemory = new UnmanagedMemoryManager(
                    (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(msg)),
                    msg.Length,
                    false); // Setting freeable to false signaling that this unmanaged memory should not be freed because it comes from an u8 literal
                
                if (!context.Connection.Write(new WriteItem(unmanagedMemory, context.Connection.ClientFd)))
                    throw new InvalidOperationException("Failed to write response");
            }
            
            // Signal that written data can be flushed
            context.Connection.Flush();
            // Signal we are ready for a new read
            context.Connection.ResetRead();
        }
    }

    // Zero allocation read and write example
    // No Peeking
    private async Task ProcessRequestsAsync(TContext context, Func<TContext, Task> pipeline)
    {
        var state = State.Headers;

        while (true) // Outer loop, iterates everytime we read more data from the wire
        {
            var result = await context.Connection.ReadAsync(); // Read data from the wire
            if (result.IsClosed)
                break;
            
            // Signals where at least one request was handled, and we can flush written response data
            var flushable = false;

            var totalAdvanced = 0;

            // Get the "head" ring - first ring received
            var rings = context.Connection.GetAllSnapshotRingsAsUnmanagedMemory(result);
            var ringsTotalLength = CalculateRingsTotalLength(rings);
            var ringCount = rings.Length;
            
            if(ringCount == 0) continue;
            
            while (true) // Inner loop, iterates every request handling, it can happen 0, 1 or n times
                // per read as we may read an incomplete, one single or multiple requests at once
            {
                bool found;
                int advanced;
                
                // Here we want to merge existing inflight data with the just read new data
                if (_inflightTail == 0)
                {
                    if (ringCount == 1)
                    {
                        // Very Hot Path, typically each ring contains a full request and inflight buffer isn't used
                        unsafe
                        {
                            var span = new ReadOnlySpan<byte>(rings[0].Ptr + totalAdvanced,
                                rings[0].Length - totalAdvanced);
                            found = HandleNoInflightSingleRing(context, ref span, out advanced);
                        }
                    }
                    else
                    {
                        // Lukewarm Path
                        found = HandleNoInflightMultipleRings(context.Connection, rings, out advanced);
                    }
                }
                else
                {
                    // Cold path
                    UnmanagedMemoryManager[] mems = new UnmanagedMemoryManager[ringCount + 1];
                    unsafe { mems[0] = new(_inflightData, _inflightTail); }
                    for (int i = 1; i < ringCount + 1; i++) mems[i] = rings[i];
                    
                    found = HandleWithInflight(context.Connection, mems, out advanced);

                    if (found)  // a request was handled so inflight data can be discarded
                        _inflightTail = 0;
                }

                totalAdvanced += advanced;

                var currentRingIndex = GetCurrentRingIndex(in totalAdvanced, rings, out var currentRingAdvanced);

                if (!found)
                {
                    unsafe
                    {
                        // \r\n\r\n not found, full headers are not yet available
                        // Copy the leftover rings data to the inflight buffer and read more

                        // Copy current ring unused data
                        Buffer.MemoryCopy(
                            rings[currentRingIndex].Ptr + currentRingAdvanced, // source
                            _inflightData + _inflightTail, // destination
                            _length - _inflightTail, // destinationSizeInBytes
                            rings[currentRingIndex].Length - currentRingAdvanced); // sourceBytesToCopy

                        _inflightTail += rings[currentRingIndex].Length - currentRingAdvanced;

                        // Copy untouched rings data
                        for (int i = currentRingIndex + 1; i < rings.Length; i++)
                        {
                            Buffer.MemoryCopy(
                                rings[i].Ptr, // source
                                _inflightData + _inflightTail, // destination
                                _length - _inflightTail, // destinationSizeInBytes
                                rings[i].Length); // sourceBytesToCopy

                            _inflightTail += rings[i].Length;
                        }
                    }

                    break;
                }

                flushable = true;
                
                if (ringsTotalLength == advanced)
                    break;
            }
                
            // Return the rings to the kernel, at this stage the request was either handled or the rings' data
            // has already been copied to the inflight buffer.
            for (int i = 0; i < rings.Length; i++) 
                context.Connection.ReturnRing(rings[i].BufferId);
            
            /*if (HandleResult(context.Connection, ref result))
            {
                context.Connection.Flush(); // Mark data to be ready to be flushed
            }*/
            if(flushable)
                context.Connection.Flush();
            context.Connection.ResetRead(); // Reset connection's ManualResetValueTaskSourceCore<ReadResult>
        }
    }
    
    private static bool HandleNoInflightSingleRing(TContext context, ref ReadOnlySpan<byte> data, out int advanced)
    {
        // Hotpath, typically each ring contains a full request and inflight buffer isn't used
        advanced = data.IndexOf("\r\n\r\n"u8);
        var found = advanced != -1;

        if (!found)
        {
            advanced = 0;
            return false;
        }
        
        advanced += 4;

        var requestSpan = data[..advanced];
        
        // Handle the request
        // ...
        if(found) WriteResponse(context.Connection); // Simulating writing the response after handling the received request

        return found;
    }

    private static bool HandleNoInflightMultipleRings(Connection connection, UnmanagedMemoryManager[] rings, out int position)
    {
        var sequence = rings.ToReadOnlySequence();
        var reader = new SequenceReader<byte>(sequence);
        var found = reader.TryReadTo(out ReadOnlySequence<byte> headersSequence, "\r\n\r\n"u8);

        if (!found)
        {
            position = 0;
            return false;
        }

        position = reader.Position.GetInteger();

        // Handle the request
        // ...
        if(found) WriteResponse(connection); // Simulating writing the response after handling the received request

        return found;
    }

    private static bool HandleWithInflight(Connection connection, UnmanagedMemoryManager[] unmanagedMemories, out int position)
    {
        var sequence = unmanagedMemories.ToReadOnlySequence();
        var reader = new SequenceReader<byte>(sequence);
        var found = reader.TryReadTo(out ReadOnlySequence<byte> headersSequence, "\r\n\r\n"u8);

        if (!found)
        {
            position = 0;
            return false;
        }

        // Calculating how many bytes from the received rings were consumed
        // inflight data is subtracted
        position = reader.Position.GetInteger() - unmanagedMemories[0].Length;

        // Handle the request
        // ...
        if(found) WriteResponse(connection); // Simulating writing the response after handling the received request

        return found;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void WriteResponse(Connection connection)
    {
        // Write the response
        var msg =
            "HTTP/1.1 200 OK\r\nContent-Length: 13\r\nContent-Type: text/plain\r\n\r\nHello, World!"u8;

        // Building an UnmanagedMemoryManager wrapping the msg, this step has no data allocation
        // however msg must be fixed/pinned because the engine reactor's needs to pass a byte* to liburing
        var unmanagedMemory = new UnmanagedMemoryManager(
            (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(msg)),
            msg.Length,
            false); // Setting freeable to false signaling that this unmanaged memory should not be freed because it comes from an u8 literal

        if (!connection.Write(new WriteItem(unmanagedMemory, connection.ClientFd)))
            throw new InvalidOperationException("Failed to write response");
    }
    
    private static int GetCurrentRingIndex(in int totalAdvanced, UnmanagedMemoryManager[] rings, out int currentRingAdvanced)
    {
        var total = 0;

        for (int i = 0; i < rings.Length; i++)
        {
            if (rings[i].Length + total >= totalAdvanced)
            {
                currentRingAdvanced = totalAdvanced - total;
                return i;
            }
            
            total += rings[i].Length;
        }

        currentRingAdvanced = -1;
        return -1;
    }

    private static int CalculateRingsTotalLength(UnmanagedMemoryManager[] rings)
    {
        var total = 0;
        for (int i = 0; i < rings.Length; i++) total += rings[i].Length;
        return total;
    }
}

/*
private unsafe bool HandleResult(Connection connection, ref ReadResult result)
    {
        // Signals where at least one request was handled, and we can flush written response data
        var flushable = false;

        var totalAdvanced = 0;

        // Get the "head" ring - first ring received
        var rings = connection.GetAllSnapshotRingsAsUnmanagedMemory(result);
        var ringsTotalLength = CalculateRingsTotalLength(rings);
        var ringCount = rings.Length;
        
        if(ringCount == 0)
            return false;
        
        while (true) // Inner loop, iterates every request handling, it can happen 0, 1 or n times
            // per read as we may read an incomplete, one single or multiple requests at once
        {
            bool found;
            int advanced;
            // Here we want to merge existing inflight data with the just read new data
            if (_inflightTail == 0)
            {
                if (ringCount == 1)
                {
                    // Very Hot Path, typically each ring contains a full request and inflight buffer isn't used
                    var span = new ReadOnlySpan<byte>(rings[0].Ptr + totalAdvanced, rings[0].Length - totalAdvanced);
                    found = HandleNoInflightSingleRing(connection, ref span, out advanced);
                }
                else
                {
                    // Lukewarm Path
                    found = HandleNoInflightMultipleRings(connection, rings, out advanced);
                }
            }
            else
            {
                // Cold path
                UnmanagedMemoryManager[] mems = new UnmanagedMemoryManager[ringCount + 1];
                mems[0] = new(_inflightData, _inflightTail);
                for (int i = 1; i < ringCount + 1; i++) mems[i] = rings[i];
                
                found = HandleWithInflight(connection, mems, out advanced);

                if (found)  // a request was handled so inflight data can be discarded
                    _inflightTail = 0;
            }

            totalAdvanced += advanced;

            var currentRingIndex = GetCurrentRingIndex(in totalAdvanced, rings, out var currentRingAdvanced);

            if (!found)
            {
                // \r\n\r\n not found, full headers are not yet available
                // Copy the leftover rings data to the inflight buffer and read more
                
                // Copy current ring unused data
                Buffer.MemoryCopy(
                    rings[currentRingIndex].Ptr + currentRingAdvanced, // source
                    _inflightData + _inflightTail, // destination
                    _length - _inflightTail, // destinationSizeInBytes
                    rings[currentRingIndex].Length - currentRingAdvanced); // sourceBytesToCopy
                
                _inflightTail += rings[currentRingIndex].Length - currentRingAdvanced;
                
                // Copy untouched rings data
                for (int i = currentRingIndex + 1; i < rings.Length; i++)
                {
                    Buffer.MemoryCopy(
                        rings[i].Ptr, // source
                        _inflightData + _inflightTail, // destination
                        _length - _inflightTail, // destinationSizeInBytes
                        rings[i].Length); // sourceBytesToCopy
                    
                    _inflightTail += rings[i].Length;
                }

                break;
            }

            flushable = true;
            
            if (ringsTotalLength == advanced)
                break;
        }
            
        // Return the rings to the kernel, at this stage the request was either handled or the rings' data
        // has already been copied to the inflight buffer.
        for (int i = 0; i < rings.Length; i++) 
            connection.ReturnRing(rings[i].BufferId);

        return flushable;
    }
    */
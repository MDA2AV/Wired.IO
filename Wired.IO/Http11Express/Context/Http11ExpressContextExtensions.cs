using System.Buffers;
using Wired.IO.Utilities;

// ReSharper disable MemberCanBePrivate.Global

namespace Wired.IO.Http11Express.Context;

public static class Http11ExpressContextExtensions
{
    public static async Task SendAsync(this Http11ExpressContext context, string response, CancellationToken cancellationToken = default)
    {
        var responseBytes = new ReadOnlyMemory<byte>(Encoders.Utf8Encoder.GetBytes(response));
        await context.SendAsync(responseBytes, cancellationToken);
    }
    
    public static async Task SendAsync(this Http11ExpressContext context, ReadOnlyMemory<byte> responseBytes, CancellationToken cancellationToken = default)
    {
        await context.Writer.WriteAsync(responseBytes, cancellationToken);
    }
    
    public static void SendAsync(this Http11ExpressContext context, ReadOnlySpan<byte> responseBytes)
    {
        context.Writer.Write(responseBytes);
    }
    
    
    public static async Task<(bool, long)> TryReadAsync(
        this Http11ExpressContext context,
        Memory<byte> buffer, 
        CancellationToken cancellationToken = default)
    {
        var result = await context.Reader.ReadAsync(cancellationToken);
        var readableBuffer = result.Buffer;
        
        if(result.Buffer.Length > buffer.Length || 
           readableBuffer.Length == 0 && result.IsCompleted) return (false, 0);

        readableBuffer.Slice(0, readableBuffer.Length).CopyTo(buffer.Span);
        context.Reader.AdvanceTo(readableBuffer.GetPosition(readableBuffer.Length));

        return (true, readableBuffer.Length);
    }
}
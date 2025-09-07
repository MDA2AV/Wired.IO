namespace Wired.IO.Protocol.Response;

public interface IBaseResponse : IDisposable
{
    /// <summary>
    /// Clears the response state of the current context without disposing it.
    /// </summary>
    /// <remarks>
    /// This method is typically used to reset the context for reuse within a connection handling loop.
    /// </remarks>
    void Clear();
}

namespace Wired.IO.Protocol.Response.Headers;

public interface IHeaderCollection : IReadOnlyDictionary<string, string>, IDisposable;
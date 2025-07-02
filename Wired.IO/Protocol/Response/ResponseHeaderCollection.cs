using Wired.IO.Protocol.Response.Headers;
using Wired.IO.Utilities;

namespace Wired.IO.Protocol.Response;

public sealed class ResponseHeaderCollection : PooledDictionary<string, string>, IHeaderCollection,
    IEditableHeaderCollection
{
}
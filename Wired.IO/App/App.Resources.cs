using System.Reflection;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    // This all might have to be static so that it can be seen by the middleware, avoiding a closure
    public bool CanServeStaticFiles { get; set; } = false;
    public bool CanServeSpaFiles { get; set; } = false;

    //public readonly List<string> StaticResourceRoutesCache = new();
    //public readonly List<string> SpaResourceRoutesCache = new();

    // Should be sorted by key size descending so that longer matches are found first
    public readonly Dictionary<string, Location> StaticResourceRouteToLocation = new();

    // Full route baseRoute + resource path
    public readonly Dictionary<string, ReadOnlyMemory<byte>> StaticCachedResourceFiles = new();
    //public readonly Dictionary<string, ReadOnlyMemory<byte>> CachedStaticSpaFiles = new();
}

public class Location
{
    public LocationType LocationType { get; set; }

    public Assembly? Assembly { get; set; }

    public string Path { get; set; } = null!;
}

public enum LocationType
{
    FileSystem,
    EmbeddedResource
}
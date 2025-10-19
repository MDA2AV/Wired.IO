using System.Reflection;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    public bool CanServeStaticFiles { get; set; } = false;
    public bool CanServeSpaFiles { get; set; } = false;
    public bool CanServeMpaFiles { get; set; } = false;

    // Should be sorted by key size descending so that longer matches are found first
    public readonly Dictionary<string, Location> StaticResourceRouteToLocation = new();

    // Full route baseRoute + resource path
    public static readonly Dictionary<string, ReadOnlyMemory<byte>> StaticCachedResourceFiles = new();

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
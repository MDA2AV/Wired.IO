using System.Reflection;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public sealed partial class WiredApp<TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
{
    /// <summary>
    /// Indicates whether static files can be served by this application.
    /// </summary>
    public bool CanServeStaticFiles { get; set; } = false;

    /// <summary>
    /// Indicates whether Single-Page Application (SPA) files can be served.
    /// </summary>
    public bool CanServeSpaFiles { get; set; } = false;

    /// <summary>
    /// Indicates whether Multi-Page Application (MPA) files can be served.
    /// </summary>
    public bool CanServeMpaFiles { get; set; } = false;

    /// <summary>
    /// Maps a base static route (e.g. "/app" or "/static") to a <see cref="Location"/> source
    /// that defines where the files are served from.  
    /// Routes should be ordered by descending key length so that longer routes
    /// take precedence during lookup.
    /// </summary>
    public readonly Dictionary<string, Location> StaticResourceRouteToLocation = new();

    /// <summary>
    /// Caches embedded or preloaded static resources, keyed by their full route
    /// (combination of base route and resource path).  
    /// This allows high-performance in-memory serving without filesystem or reflection access.
    /// </summary>
    public static readonly Dictionary<string, ReadOnlyMemory<byte>> StaticCachedResourceFiles = new();
}

/// <summary>
/// Describes the origin of static or embedded resources within a Wired.IO application.
/// </summary>
public class Location
{
    /// <summary>
    /// The type of location where resources are stored,  
    /// either in the local file system or as embedded resources in an assembly.
    /// </summary>
    public LocationType LocationType { get; set; }

    /// <summary>
    /// The <see cref="Assembly"/> containing the embedded resources,  
    /// when <see cref="LocationType"/> is <see cref="LocationType.EmbeddedResource"/>.  
    /// This property is <see langword="null"/> for file system locations.
    /// </summary>
    public Assembly? Assembly { get; set; }

    /// <summary>
    /// The physical path (for file system locations) or the base namespace path  
    /// (for embedded resource locations).
    /// </summary>
    public string Path { get; set; } = null!;
}

/// <summary>
/// Specifies the type of source used for serving static resources.
/// </summary>
public enum LocationType
{
    /// <summary>
    /// Resources are served directly from the local file system.
    /// </summary>
    FileSystem,

    /// <summary>
    /// Resources are served from embedded resources within an assembly.
    /// </summary>
    EmbeddedResource
}
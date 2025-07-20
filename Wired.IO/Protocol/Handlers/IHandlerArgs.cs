using System.Reflection;
using Wired.IO.Http11;

namespace Wired.IO.Protocol.Handlers;

/// <summary>
/// Represents a set of configuration parameters used by request handlers.
/// </summary>
public interface IHandlerArgs
{
    /// <summary>
    /// Gets a value indicating whether embedded resources should be used to serve static content.
    /// </summary>
    /// <remarks>
    /// When set to <c>true</c>, the server will attempt to resolve requests using embedded resources
    /// instead of or in addition to the file system.
    /// </remarks>
    bool UseResources { get; }

    /// <summary>
    /// Gets the relative or virtual path within the assembly where embedded resources are located.
    /// </summary>
    /// <remarks>
    /// This path is typically used to construct resource names when loading embedded files via reflection.
    /// Example: "MyApp.Resources".
    /// </remarks>
    string ResourcesPath { get; }

    /// <summary>
    /// Gets the <see cref="System.Reflection.Assembly"/> that contains the embedded resources.
    /// </summary>
    /// <remarks>
    /// This assembly is used to retrieve resource streams for static file responses
    /// when <see cref="UseResources"/> is enabled.
    /// </remarks>
    Assembly ResourcesAssembly { get; }

    Http11HandlerType HandlerType { get; }
}
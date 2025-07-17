namespace Wired.IO.Common.Attributes;

/// <summary>
/// Specifies the HTTP method and route pattern for an endpoint handler.
/// </summary>
/// <remarks>
/// This attribute is used to decorate endpoint methods to indicate the HTTP method
/// (e.g., "GET", "POST") and route pattern (e.g., "/users/{id}") they respond to.
/// It enables Wired.IO to automatically map incoming requests to the correct handler.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class RouteAttribute(string httpMethod, string route) 
    : Attribute
{
    /// <summary>
    /// Gets or sets the HTTP method associated with the route.
    /// </summary>
    public string HttpMethod { get; set; } = httpMethod;

    /// <summary>
    /// Gets or sets the route pattern for the endpoint.
    /// </summary>
    public string Route { get; set; } = route;
}
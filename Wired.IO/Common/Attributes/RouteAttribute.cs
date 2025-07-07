namespace Wired.IO.Common.Attributes;

public class RouteAttribute(string httpMethod, string route) 
    : Attribute
{
    public string HttpMethod { get; set; } = httpMethod;
    public string Route { get; set; } = route;
}
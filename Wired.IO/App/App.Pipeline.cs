using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.Protocol.Response;

namespace Wired.IO.App;

public partial class App<TContext>
{
    public Task<IResponse> Pipeline(
        TContext context,
        int index,
        IList<Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>> middleware)
    {
        if (index < middleware.Count)
        {
            return middleware[index](context, async (ctx) => await Pipeline(ctx, index + 1, middleware));
        }

        var decodedRoute = MatchEndpoint(EncodedRoutes[context.Request.HttpMethod.ToUpper()], context.Request.Route);

        var endpoint = context.Scope.ServiceProvider
            .GetRequiredKeyedService<Func<TContext, Task<IResponse>>>(
                $"{context.Request.HttpMethod}_{decodedRoute}");

        return endpoint.Invoke(context);
    }

    public async Task<IResponse> Pipeline(TContext context)
    {
        var middleware = InternalHost.Services
            .GetServices<Func<TContext, Func<TContext, Task<IResponse>>, Task<IResponse>>>()
            .ToList();

        await using var scope = InternalHost.Services.CreateAsyncScope();
        context.Scope = scope;

        return await Pipeline(context, 0, middleware);
    }

    public Task PipelineNoResponse(
        TContext context,
        int index,
        IList<Func<TContext, Func<TContext, Task>, Task>> middleware)
    {
        if (index < middleware.Count)
        {
            return middleware[index](context, async (ctx) => await PipelineNoResponse(ctx, index + 1, middleware));
        }

        var httpMethod = context.Request.HttpMethod.ToUpper();
        var decodedRoute = MatchEndpoint(EncodedRoutes[httpMethod], context.Request.Route);

        var endpoint = Endpoints[httpMethod + "_" + decodedRoute!];

        //var endpoint = context.Scope.ServiceProvider
        //    .GetRequiredKeyedService<Func<TContext, Task>>($"{context.Request.HttpMethod}_{decodedRoute}");

        return endpoint is null
            ? throw new InvalidOperationException("Unable to find the Invoke method on the resolved service.")
            : endpoint.Invoke(context);
    }

    public async Task PipelineNoResponse(TContext context)
    {
        await using var scope = InternalHost.Services.CreateAsyncScope();
        context.Scope = scope;

        await PipelineNoResponse(context, 0, Middleware);
    }

    public static string? MatchEndpoint(HashSet<string> hashSet, string input)
    {
        return (from entry in hashSet
                let pattern = ConvertToRegex(entry) // Convert route pattern to regex
                where Regex.IsMatch(input, pattern) // Check if input matches the regex
                select entry) // Select the matching pattern
            .FirstOrDefault(); // Return the first match or null if no match is found
    }

    public static string ConvertToRegex(string pattern)
    {
        // Replace placeholders like ":id" with a regex pattern that matches any non-slash characters
        var regexPattern = Regex.Replace(pattern, @":\w+", "[^/]+");

        // Add anchors to ensure the regex matches the entire input string
        regexPattern = $"^{regexPattern}$";

        return regexPattern;
    }
}
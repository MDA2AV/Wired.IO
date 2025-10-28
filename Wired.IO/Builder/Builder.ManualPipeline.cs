using System;
using Microsoft.Extensions.DependencyInjection;
using Wired.IO.App;
using Wired.IO.Http11Express.Context;
using Wired.IO.Protocol;
using Wired.IO.Protocol.Handlers;
using Wired.IO.Protocol.Request;
using Wired.IO.Protocol.Response;

namespace Wired.IO.Builder;

public sealed partial class Builder<THandler, TContext>
    where TContext : IBaseContext<IBaseRequest, IBaseResponse>
    where THandler : IHttpHandler<TContext>
{
    public Builder<THandler, TContext> AddManualPipeline(
            string route,
            List<string> keys,
            Func<TContext, Task> endpoint,
            List<Func<TContext, Func<TContext, Task>, Task>>? middlewares)
    {

        foreach (var key in keys)
        {
            if (App.EncodedRoutes.TryGetValue(key, out var value))
            {
                value.Add(route);

                var endpointKey = new EndpointKey(key, route);

                App.ManualPipelineEntries.Add(new WiredApp<TContext>.ManualPipelineEntry
                {
                    EndpointKey = endpointKey,
                    Middlewares = middlewares
                });

                App.ServiceCollection.AddKeyedScoped<Func<TContext, Task>>(new EndpointKey(key, route), (_, _) => endpoint);
            }
        }

        return this;
    }
}
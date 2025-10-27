using Wired.IO.App;
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
        string key, 
        string route, 
        List<Func<TContext, Func<TContext, Task>, Task>>? middlewares, 
        bool partialMatch = false)
    {
        if (partialMatch)
        {
            if (App.PartialExactMatchRoutes.TryGetValue(key, out var value))
                value.Add(route);
            else
                App.PartialExactMatchRoutes.Add(key, [route]);
        }
        else
        {
            if (App.EncodedRoutes.TryGetValue(key, out var value))
                value.Add(route);
            else
                App.EncodedRoutes.Add(key, [route]);
        }
            
        var endpointKey = new EndpointKey(key, route);

        App.ManualPipelineEntries.Add(new WiredApp<TContext>.ManualPipelineEntry
        {
            EndpointKey = endpointKey,
            Middlewares = middlewares
        });

        return this;
    }
}
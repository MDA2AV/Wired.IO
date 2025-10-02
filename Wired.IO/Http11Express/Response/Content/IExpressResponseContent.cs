using System.IO.Pipelines;
using System.Text.Json.Serialization.Metadata;

namespace Wired.IO.Http11Express.Response.Content;

public interface IExpressResponseContent
{
    ulong? Length { get; }

    void Write(PipeWriter writer);
}

public interface IExpressResponseContent<TPayload> : IExpressResponseContent
{
    IExpressResponseContent<TPayload> Set(TPayload data, JsonTypeInfo<TPayload>? typeInfo = null, ulong? length = null);
}
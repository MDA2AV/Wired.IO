using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Wired.IO.Http11Express.Response;
using Wired.IO.Http11Express.Response.Content;
using Wired.IO.Utilities;

namespace Wired.IO.Protocol.Response;

public enum ContentLengthStrategy
{
    None,
    Known,
    Utf8View,
    Action,
    AsyncTask,
    Chunked,
}

public class ExpressResponseBuilder(IExpressResponse response)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetContentLength(IExpressResponseContent content)
    {
        response.ContentLength = content.Length;
        response.ContentLengthStrategy = content.Length is not null
            ? ContentLengthStrategy.Known
            : ContentLengthStrategy.Chunked;
    }
    
    public ExpressResponseBuilder Content(IExpressResponseContent content)
    {
        response.Content = content;
        response.ContentLength = content.Length;

        response.ContentLengthStrategy = content.Length is not null
            ? ContentLengthStrategy.Known
            : ContentLengthStrategy.Chunked;

        return this;
    }

    public ExpressResponseBuilder Content(ReadOnlySpan<byte> content)
    {
        response.Utf8Content = Utf8View.FromLiteral(content);
        response.ContentLength = (ulong)content.Length;

        response.ContentLengthStrategy = ContentLengthStrategy.Utf8View;

        return this;
    }

    public ExpressResponseBuilder Content(Action handler, ulong? length = null)
    {
        response.ContentLength = length;
        response.ContentLengthStrategy = ContentLengthStrategy.Action;
        
        response.Handler = handler;
        
        return this;
    }
    
    public ExpressResponseBuilder Content(Func<Task> handler, ulong? length = null)
    {
        response.ContentLength = length;
        response.ContentLengthStrategy = ContentLengthStrategy.AsyncTask;
        
        response.AsyncHandler = handler;
        
        return this;
    }

    public ExpressResponseBuilder Content<TContent, TPayload>(
        Action<TContent, TPayload> setup, 
        TPayload payload, 
        Func<TPayload, ulong, TContent> contentFactory, 
        ulong length)
        where  TContent : IExpressResponseContent
    {
        if (response.Content is not TContent content) 
            return Content(contentFactory(payload, length));
        
        setup(content, payload);
        SetContentLength(content);
        
        return this;
    }
    
    public ExpressResponseBuilder Content<TContent, TPayload>(
        Action<TContent, TPayload> setup, 
        TPayload payload, 
        Func<TPayload, ulong?, TContent> contentFactory)
        where  TContent : IExpressResponseContent
    {
        if (response.Content is not TContent content) 
            return Content(contentFactory(payload, null));
        
        setup(content, payload);
        SetContentLength(content);
        
        return this;
    }

    public ExpressResponseBuilder Content<TContent, TPayload>(
        Action<TContent, TPayload, JsonTypeInfo<TPayload>, ulong?> setup,
        TPayload payload,
        JsonTypeInfo<TPayload> typeInfo,
        Func<TPayload, JsonTypeInfo<TPayload>, ulong, TContent> contentFactory,
        ulong length)
        where TContent : IExpressResponseContent
    {
        if (response.Content is not TContent content)
            return Content(contentFactory(payload, typeInfo, length));

        setup(content, payload, typeInfo, length);
        SetContentLength(content);

        return this;
    }

    public ExpressResponseBuilder Content<TContent, TPayload>(
        Action<TContent, TPayload, JsonTypeInfo<TPayload>> setup,
        TPayload payload,
        JsonTypeInfo<TPayload> typeInfo,
        Func<TPayload, JsonTypeInfo<TPayload>, TContent> contentFactory)
        where TContent : IExpressResponseContent
    {
        if (response.Content is not TContent content)
            return Content(contentFactory(payload, typeInfo));

        setup(content, payload, typeInfo);
        SetContentLength(content);

        return this;
    }

    public ExpressResponseBuilder Type(ReadOnlySpan<byte> contentType)
    {
        response.ContentType = Utf8View.FromLiteral(contentType);
        return this;
    }

    public ExpressResponseBuilder Status(ResponseStatus status)
    {
        response.Status = status;
        return this;
    }

    public ExpressResponseBuilder Header(string key, string value)
    {
        response.Headers.Add(key, value);
        return this;
    }

    public ExpressResponseBuilder Expires(DateTime expiryDate)
    {
        response.Expires = expiryDate;
        return this;
    }

    public ExpressResponseBuilder Modified(DateTime modificationDate)
    {
        response.Modified = modificationDate;
        return this;
    }
    
    public ExpressResponseBuilder Encoding(string encoding)
    {
        response.ContentEncoding = encoding;
        return this;
    }

    public ExpressResponseBuilder Length(ulong length)
    {
        response.ContentLength = length;
        return this;
    }
}
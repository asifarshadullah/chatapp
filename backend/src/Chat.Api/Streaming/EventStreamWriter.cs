using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace Chat.Api.Streaming;

/// <summary>
/// Writes server-sent events, flushing each one. Data is JSON so a token's line breaks stay on one line.
/// </summary>
public sealed class EventStreamWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpResponse _response;

    private EventStreamWriter(HttpResponse response) => _response = response;

    /// <summary>Call only after every refusal: the first event commits the response to 200.</summary>
    public static EventStreamWriter Start(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        // Stops nginx-style proxies buffering the stream.
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        return new EventStreamWriter(response);
    }

    public async Task WriteAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        var frame = $"event: {eventName}\ndata: {JsonSerializer.Serialize(data, JsonOptions)}\n\n";
        await _response.WriteAsync(frame, cancellationToken);
        await _response.Body.FlushAsync(cancellationToken);
    }
}

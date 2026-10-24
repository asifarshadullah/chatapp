using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>One server-sent event: its name and its raw <c>data</c>.</summary>
public record ServerSentEvent(string Event, string Data)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public T Json<T>() => JsonSerializer.Deserialize<T>(Data, JsonOptions)!;
}

/// <summary>Sends chat messages and reads the reply's events as they arrive.</summary>
public static class EventStream
{
    public const string MessagesPath = "/api/chat/messages";

    /// <summary>Returns once the headers arrive, leaving the body to be read event by event.</summary>
    public static Task<HttpResponseMessage> PostMessageAsync(
        HttpClient client, object body, Guid? user = null, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MessagesPath) { Content = JsonContent.Create(body) };
        if (user is not null)
            request.Headers.Add(TestAuthHandler.UserHeader, user.Value.ToString());
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public static async IAsyncEnumerable<ServerSentEvent> ReadEventsAsync(
        HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? name = null;
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (name is not null || data.Length > 0)
                    yield return new ServerSentEvent(name ?? "message", data.ToString());
                name = null;
                data.Clear();
            }
            else if (line.StartsWith("event:"))
            {
                name = line["event:".Length..].TrimStart();
            }
            else if (line.StartsWith("data:"))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line["data:".Length..].TrimStart());
            }
        }
    }

    public static async Task<List<ServerSentEvent>> ReadAllEventsAsync(
        HttpResponseMessage response, CancellationToken ct = default)
    {
        var events = new List<ServerSentEvent>();
        await foreach (var e in ReadEventsAsync(response, ct))
            events.Add(e);
        return events;
    }
}

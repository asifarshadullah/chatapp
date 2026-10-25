using System.Net;
using System.Text;
using System.Threading.Channels;

namespace Chat.Infrastructure.Tests.AI;

/// <summary>
/// Stands in for a chat completions service: records each request and answers with a status and
/// an event stream whose events the test releases, so it can show tokens are not buffered.
/// </summary>
public sealed class FakeChatCompletionsHandler : HttpMessageHandler
{
    private readonly Channel<string> _events = Channel.CreateUnbounded<string>();

    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public string ErrorBody { get; init; } = """{"error":{"message":"Rate limit reached"}}""";

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public static string Content(string text) =>
        Chunk($$"""{"role":"assistant","content":{{System.Text.Json.JsonSerializer.Serialize(text)}}}""");

    public static string Reasoning(string text) =>
        Chunk($$"""{"reasoning":{{System.Text.Json.JsonSerializer.Serialize(text)}}}""");

    private static string Chunk(string delta) =>
        $$"""data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{{delta}},"finish_reason":null}]}""" + "\n\n";

    public const string Done = "data: [DONE]\n\n";

    public void Release(params string[] events)
    {
        foreach (var e in events) _events.Writer.TryWrite(e);
    }

    public void Finish() => _events.Writer.TryComplete();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));

        if (Status != HttpStatusCode.OK)
            return new HttpResponseMessage(Status) { Content = new StringContent(ErrorBody, Encoding.UTF8, "application/json") };

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EventStream(_events.Reader)) };
        response.Content.Headers.ContentType = new("text/event-stream");
        return response;
    }

    private sealed class EventStream(ChannelReader<string> events) : Stream
    {
        private byte[] _pending = [];
        private int _offset;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_offset == _pending.Length)
            {
                if (!await events.WaitToReadAsync(ct) || !events.TryRead(out var next)) return 0;
                _pending = Encoding.UTF8.GetBytes(next);
                _offset = 0;
            }
            var count = Math.Min(buffer.Length, _pending.Length - _offset);
            _pending.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chat.Api.Tests.Infrastructure;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Tests.Controllers;

/// <summary>
/// GET /api/chat/conversations: the caller's conversations with a reply, newest first, a page at
/// a time. Each test acts as its own user, so the shared fixture's data never mixes.
/// </summary>
public class ConversationListTests : IClassFixture<ChatApiFactory>
{
    private const string ListPath = "/api/chat/conversations";
    private static readonly DateTime Start = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ChatApiFactory _factory;
    private readonly HttpClient _client;

    public ConversationListTests(ChatApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Entry(Guid Id, string Title, DateTime LastActivityAt);
    private record Page(List<Entry> Conversations, string? NextCursor);

    private Task<HttpResponseMessage> ListAs(Guid user, string query = "") =>
        ListAs(_client, user, query);

    private static Task<HttpResponseMessage> ListAs(HttpClient client, Guid user, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ListPath + query);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        return client.SendAsync(request);
    }

    private async Task<Page> PageAs(Guid user, string query = "")
    {
        var response = await ListAs(user, query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Page>())!;
    }

    // Seeds straight into the repository, with a reply at the given time.
    private async Task<Guid> Seed(Guid user, string title, DateTime at)
    {
        var conversation = await _factory.Repository.CreateConversationAsync(user, title);
        await _factory.Repository.AddMessageAsync(conversation.Id, user, new ChatMessage(Guid.NewGuid(), "Q", MessageRole.User, at.AddSeconds(-1)));
        await _factory.Repository.AddMessageAsync(conversation.Id, user, new ChatMessage(Guid.NewGuid(), "A", MessageRole.Assistant, at));
        return conversation.Id;
    }

    // ── What the list holds ──────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsIdTitleAndActivity_NewestFirst_WithoutMessages()
    {
        var user = Guid.NewGuid();
        var older = await Seed(user, "Older", Start);
        var newer = await Seed(user, "Newer", Start.AddHours(1));

        var response = await ListAs(user);
        var json = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<Page>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        page.Conversations.Select(c => (c.Id, c.Title)).Should().Equal((newer, "Newer"), (older, "Older"));
        page.NextCursor.Should().BeNull();
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("conversations")[0];
        first.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "title", "lastActivityAt");
        first.GetProperty("lastActivityAt").GetString().Should().EndWith("Z");
    }

    [Fact]
    public async Task List_ForAUserWithNoConversations_IsEmptyWithANullCursor()
    {
        var page = await PageAs(Guid.NewGuid());

        page.Conversations.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task List_FollowsTheCursorThroughTwentyFive_EachOnce()
    {
        var user = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++)
            ids.Add(await Seed(user, $"C{i}", Start.AddMinutes(i)));

        var first = await PageAs(user);
        var second = await PageAs(user, $"?cursor={Uri.EscapeDataString(first.NextCursor!)}");

        first.Conversations.Should().HaveCount(20);
        second.Conversations.Should().HaveCount(5);
        second.NextCursor.Should().BeNull();
        first.Conversations.Concat(second.Conversations).Select(c => c.Id).Should().Equal(Enumerable.Reverse(ids));
    }

    [Fact]
    public async Task List_WithASmallerLimit_ReturnsThatManyAndACursor()
    {
        var user = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            await Seed(user, $"C{i}", Start.AddMinutes(i));

        var page = await PageAs(user, "?limit=2");

        page.Conversations.Should().HaveCount(2);
        page.NextCursor.Should().NotBeNull();
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    public async Task List_WithALimitOutOfRange_IsBadRequest(string limit)
    {
        var response = await ListAs(Guid.NewGuid(), $"?limit={limit}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail
            .Should().Be("Limit must be between 1 and 50.");
    }

    [Fact]
    public async Task List_WithALimitThatIsNotANumber_IsBadRequest()
    {
        var response = await ListAs(Guid.NewGuid(), "?limit=abc");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public static TheoryData<string> MalformedCursors => new()
    {
        "not base64 !",
        Base64Url("abc:" + Guid.NewGuid()),
        Base64Url("638000000000000000:not-a-guid"),
    };

    [Theory]
    [MemberData(nameof(MalformedCursors))]
    public async Task List_WithAMalformedCursor_IsBadRequest(string cursor)
    {
        var response = await ListAs(Guid.NewGuid(), $"?cursor={Uri.EscapeDataString(cursor)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Be("Invalid cursor.");
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── Through the send path ────────────────────────────────────────────────

    private static async Task SendAs(HttpClient client, Guid user, string message)
    {
        using var response = await EventStream.PostMessageAsync(client, new { message }, user);
        await EventStream.ReadAllEventsAsync(response);
    }

    [Fact]
    public async Task AFirstMessage_IsListedWithTheGeneratedTitle()
    {
        var user = Guid.NewGuid();

        await SendAs(_client, user, "What is a monad?");

        (await PageAs(user)).Conversations.Single().Title.Should().Be(FakeTitleGenerator.DefaultTitle);
    }

    [Fact]
    public async Task WhenTheTitleFails_TheStreamStillEndsWithDone_AndTheFirstMessageIsTheTitle()
    {
        await using var factory = new ChatApiFactory();
        factory.Titles.Title = null;
        var client = factory.CreateClient();
        var user = Guid.NewGuid();

        using var response = await EventStream.PostMessageAsync(client, new { message = "What is a monad?" }, user);
        var events = await EventStream.ReadAllEventsAsync(response);

        events.Select(e => e.Event).Should().EndWith("done").And.NotContain("error");
        var page = await (await ListAs(client, user)).Content.ReadFromJsonAsync<Page>();
        page!.Conversations.Single().Title.Should().Be("What is a monad?");
    }

    [Fact]
    public async Task AFirstReplyThatFailsBeforeItsFirstToken_IsNotListed()
    {
        await using var factory = new ChatApiFactory();
        factory.Ai.Script = _ => Fail(afterTokens: []);
        var user = Guid.NewGuid();

        await SendAs(factory.CreateClient(), user, "Hello?");

        factory.Repository.ConversationsCreated.Should().Be(1);
        (await (await ListAs(factory.CreateClient(), user)).Content.ReadFromJsonAsync<Page>())!
            .Conversations.Should().BeEmpty();
    }

    [Fact]
    public async Task AFirstReplyThatFailsPartWay_IsListed()
    {
        await using var factory = new ChatApiFactory();
        factory.Ai.Script = _ => Fail(afterTokens: ["Hel"]);
        var user = Guid.NewGuid();

        await SendAs(factory.CreateClient(), user, "Hello?");

        (await (await ListAs(factory.CreateClient(), user)).Content.ReadFromJsonAsync<Page>())!
            .Conversations.Should().ContainSingle().Which.Title.Should().Be("Hello?");
    }

    private static async IAsyncEnumerable<string> Fail(string[] afterTokens)
    {
        foreach (var token in afterTokens)
            yield return token;
        await Task.Yield();
        throw new InvalidOperationException("model failed");
    }
}

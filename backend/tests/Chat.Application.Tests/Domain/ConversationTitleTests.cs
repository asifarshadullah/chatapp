using System.Text;
using Chat.Domain.Entities;
using FluentAssertions;

namespace Chat.Application.Tests.Domain;

/// <summary>
/// The title rule shared by the first-message title, the model's title and the startup backfill.
/// </summary>
public class ConversationTitleTests
{
    private const string Emoji = "😀";

    private static int CodePoints(string text) => text.EnumerateRunes().Count();

    // ── From ─────────────────────────────────────────────────────────────────

    [Fact]
    public void From_TrimsAndCollapsesWhitespace()
    {
        ConversationTitle.From("  What is\n  a monad? ").Should().Be("What is a monad?");
    }

    [Fact]
    public void From_KeepsSixtyCharactersWhole()
    {
        var text = new string('a', 30) + " " + new string('b', 29);

        ConversationTitle.From(text).Should().Be(text);
    }

    [Fact]
    public void From_CutsALongerTextAtTheLastSpaceWithinFiftyNine()
    {
        var text = "The quick brown fox jumps over the lazy dog and keeps running far away";

        var title = ConversationTitle.From(text);

        title.Should().Be("The quick brown fox jumps over the lazy dog and keeps…");
        CodePoints(title).Should().BeLessThanOrEqualTo(60);
    }

    [Fact]
    public void From_CutsAtFiftyNineWhenThereIsNoSpace()
    {
        ConversationTitle.From(new string('x', 80)).Should().Be(new string('x', 59) + "…");
    }

    [Fact]
    public void From_NeverSplitsAnEmoji()
    {
        var title = ConversationTitle.From(new string('a', 58) + string.Concat(Enumerable.Repeat(Emoji, 5)));

        title.Should().Be(new string('a', 58) + Emoji + "…");
        title.EnumerateRunes().Should().NotContain(Rune.ReplacementChar);
        Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(title)).Should().Be(title, "the title must be valid UTF-16");
    }

    [Fact]
    public void From_CountsAnEmojiAsOneCharacter()
    {
        // 59 letters and one emoji is 60 code points, though 61 UTF-16 units.
        var text = new string('a', 59) + Emoji;

        ConversationTitle.From(text).Should().Be(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void From_WhitespaceOnly_GivesEmpty(string text)
    {
        ConversationTitle.From(text).Should().BeEmpty();
    }

    // ── Clean ────────────────────────────────────────────────────────────────

    [Fact]
    public void Clean_KeepsTheFirstLineThatIsNotBlank_AndRemovesItsDecoration()
    {
        ConversationTitle.Clean("\n  \nTitle: “Understanding Monads.”\nA second line")
            .Should().Be("Understanding Monads");
    }

    [Theory]
    [InlineData("\"x\"")]
    [InlineData("'x'")]
    [InlineData("“x”")]
    [InlineData("‘x’")]
    [InlineData("`x`")]
    public void Clean_RemovesSurroundingQuotes(string answer)
    {
        ConversationTitle.Clean(answer).Should().Be("x");
    }

    [Fact]
    public void Clean_RemovesALeadingTitleInAnyCase()
    {
        ConversationTitle.Clean("title: x").Should().Be("x");
    }

    [Fact]
    public void Clean_RemovesOnlyOneTrailingFullStop()
    {
        ConversationTitle.Clean("x..").Should().Be("x.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n \n")]
    [InlineData("\"\"")]
    [InlineData("“”")]
    public void Clean_NothingLeft_GivesEmpty(string answer)
    {
        ConversationTitle.Clean(answer).Should().BeEmpty();
    }

    [Fact]
    public void Clean_CutsALongAnswerAsFromDoes()
    {
        var answer = new string('x', 80);

        ConversationTitle.Clean(answer).Should().Be(ConversationTitle.From(answer));
    }
}

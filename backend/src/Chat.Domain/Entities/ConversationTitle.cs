using System.Text;

namespace Chat.Domain.Entities;

/// <summary>
/// Turns a first message, or a model's answer to a title request, into a conversation title.
/// </summary>
public static class ConversationTitle
{
    /// <summary>The longest title, in Unicode code points, the cut marker included.</summary>
    public const int MaxLength = 60;

    private const string CutMarker = "…";
    private const string TitlePrefix = "Title:";

    private static readonly (char Open, char Close)[] Quotes =
        [('"', '"'), ('\'', '\''), ('“', '”'), ('‘', '’'), ('`', '`')];

    /// <summary>
    /// Trims the text, collapses every run of whitespace to one space and, when the result is longer
    /// than <see cref="MaxLength"/>, cuts it at a word boundary and marks the cut.
    /// </summary>
    public static string From(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Counted in runes, so a cut never leaves half of a surrogate pair behind.
        var runes = collapsed.EnumerateRunes().ToArray();
        if (runes.Length <= MaxLength)
            return collapsed;

        var head = runes[..(MaxLength - 1)];
        var lastSpace = Array.LastIndexOf(head, new Rune(' '));
        var kept = lastSpace > 0 ? head[..lastSpace] : head;
        return string.Concat(kept.Select(r => r.ToString())) + CutMarker;
    }

    /// <summary>
    /// Reduces a model's answer to a title: its first line that is not blank, without a leading
    /// "Title:", surrounding quotes or one trailing full stop, cut as <see cref="From"/> cuts.
    /// </summary>
    public static string Clean(string answer)
    {
        var line = answer.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        if (line.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase))
            line = line[TitlePrefix.Length..].Trim();

        foreach (var (open, close) in Quotes)
        {
            if (line.Length >= 2 && line[0] == open && line[^1] == close)
            {
                line = line[1..^1].Trim();
                break;
            }
        }

        if (line.EndsWith('.'))
            line = line[..^1];

        return From(line);
    }
}

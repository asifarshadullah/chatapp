using System.Text;
using Chat.Application.Exceptions;

namespace Chat.Application.Paging;

/// <summary>
/// The position after the last conversation of a page: its last activity and id. It names no
/// owner, so it can only ever position the caller within their own list.
/// </summary>
public readonly record struct ConversationCursor(DateTime LastActivityAt, Guid Id)
{
    /// <summary>Encodes the cursor as an opaque, URL-safe string.</summary>
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{LastActivityAt.Ticks}:{Id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes a cursor this struct encoded.</summary>
    /// <exception cref="InvalidCursorException">Thrown for anything else.</exception>
    public static ConversationCursor Decode(string cursor)
    {
        var base64 = cursor.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        var bytes = new byte[base64.Length];
        if (cursor.Contains('+') || cursor.Contains('/') || !Convert.TryFromBase64String(base64, bytes, out var written))
            throw new InvalidCursorException();

        var parts = Encoding.UTF8.GetString(bytes, 0, written).Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
            || !Guid.TryParse(parts[1], out var id))
            throw new InvalidCursorException();

        return new ConversationCursor(new DateTime(ticks, DateTimeKind.Utc), id);
    }
}

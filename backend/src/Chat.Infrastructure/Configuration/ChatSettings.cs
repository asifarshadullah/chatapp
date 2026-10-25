namespace Chat.Infrastructure.Configuration;

/// <summary>Chat rules bound from the <c>Chat</c> section.</summary>
public class ChatSettings
{
    /// <summary>Messages each user may send per UTC day, whichever model answers.</summary>
    public int DailyMessageLimit { get; set; } = 50;
}

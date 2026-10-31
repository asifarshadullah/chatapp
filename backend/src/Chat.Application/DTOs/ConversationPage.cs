namespace Chat.Application.DTOs;

/// <summary>A conversation as the conversation list shows it: no messages.</summary>
/// <param name="Id">The conversation's unique identifier.</param>
/// <param name="Title">Its title.</param>
/// <param name="LastActivityAt">UTC time of its latest message.</param>
public record ConversationSummary(Guid Id, string Title, DateTime LastActivityAt);

/// <summary>One page of the caller's conversations, most recently active first.</summary>
/// <param name="Conversations">The conversations on this page.</param>
/// <param name="NextCursor">Passed back to get the next page; null on the last page.</param>
public record ConversationPage(IReadOnlyList<ConversationSummary> Conversations, string? NextCursor);

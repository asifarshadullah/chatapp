namespace Chat.Application.Exceptions;

/// <summary>Thrown when a user has already sent today's allowed number of messages.</summary>
public class DailyMessageLimitReachedException : Exception
{
    /// <summary>Creates the exception for the given daily limit.</summary>
    public DailyMessageLimitReachedException(int limit)
        : base($"The daily limit of {limit} messages has been reached.")
    {
        Limit = limit;
    }

    public int Limit { get; }
}

namespace Chat.Application.Exceptions;

/// <summary>Thrown when a user has already sent today's allowed number of messages.</summary>
public class DailyMessageLimitReachedException : Exception
{
    /// <summary>Creates the exception for the given daily limit and the one upgrading would give.</summary>
    public DailyMessageLimitReachedException(int limit, int? upgradeLimit)
        : base($"The daily limit of {limit} messages has been reached.")
    {
        Limit = limit;
        UpgradeLimit = upgradeLimit;
    }

    public int Limit { get; }

    /// <summary>The limit upgrading would give, or null when there is nothing to upgrade to.</summary>
    public int? UpgradeLimit { get; }
}
